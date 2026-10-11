using System.Collections.Generic;
using System.IO;
using LifeOfEmpires;
using LifeOfEmpires.UI;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace LifeOfEmpires.Tests.EditMode
{
    /// <summary>
    /// The three metagame screens built from code against an in-memory <see cref="TestWorld"/> database.
    /// These cover what the screens do with the live save and the runtime systems — lock the rows the
    /// save has not unlocked, park a mission through <see cref="CampaignManager"/> instead of starting a
    /// battle, and refuse a kingdom purchase the player cannot afford — not pixel geometry, which
    /// batchmode cannot see.
    ///
    /// Every write redirects through <see cref="SaveSystem.OverridePath"/> to a scratch file that is
    /// deleted in teardown, so no test can ever leave a real save behind.
    /// </summary>
    public class CampaignMapScreenTests
    {
        private string _tempPath;
        private readonly List<Object> _content = new List<Object>();

        [SetUp]
        public void SetUp()
        {
            TestWorld.Build();
            AppendContent(TestWorld.Database);

            _tempPath = Path.Combine(Application.persistentDataPath, "life_of_empires_menu_test_save.json");
            if (File.Exists(_tempPath)) File.Delete(_tempPath);
            SaveSystem.OverridePath = _tempPath;

            CampaignManager.ClearPendingMission();
            CampaignMapScreen.s_titleShown = false;
        }

        [TearDown]
        public void TearDown()
        {
            SaveSystem.OverridePath = null;
            if (File.Exists(_tempPath)) File.Delete(_tempPath);

            CampaignManager.ClearPendingMission();

            for (int i = 0; i < _content.Count; i++)
            {
                if (_content[i] != null) Object.DestroyImmediate(_content[i]);
            }

            _content.Clear();
            TestWorld.TearDown();
        }

        [Test]
        public void CampaignMap_BuildsARowPerMission()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Assert.AreEqual(TestWorld.Database.missions.Count, screen.state.Rows.Count,
                "the map must show one row per authored mission");
            Assert.AreSame(TestWorld.Database.missions[0], screen.state.Rows[0].Mission,
                "the first row must be the first mission");
        }

        [Test]
        public void CampaignMap_LocksRowsTheSaveHasNotUnlocked()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Assert.AreEqual(1, CountInteractable(screen), "on a fresh save only the first mission is open");

            var save = new SaveData();
            save.RecordMissionResult("mission_01", completed: true, stars: 3, seconds: 30f);
            SaveSystem.WriteToDisk(save);

            screen.Refresh();

            Assert.IsTrue(screen.state.Rows[1].Button.interactable,
                "completing mission_01 must make the second row playable");
        }

        [Test]
        public void CampaignMap_PlaySelectedRefusesALockedMission()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            screen.SelectRow(1);
            Assert.IsFalse(screen.state.Rows[1].Button.interactable, "precondition: the second mission is locked");

            screen.PlaySelected();

            Assert.IsNull(CampaignManager.PendingMissionId, "a locked mission must never be parked for battle");
        }

        [Test]
        public void CampaignMap_PlaySelectedParksTheMissionForTheBattle()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            screen.SelectRow(0);
            screen.PlaySelected();

            Assert.AreEqual(TestWorld.Database.missions[0].missionId, CampaignManager.PendingMissionId,
                "an unlocked row must park its mission for the battle to pick up");
        }

        [Test]
        public void CampaignMap_MapSurfaceKeepsEveryRowAddressable()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            for (int i = 0; i < screen.state.Rows.Count; i++)
            {
                Button button = screen.state.Rows[i].Button;
                Assert.IsNotNull(button, $"row {i} must carry a Button");

                // The plate must live inside the map surface — walking ancestors keeps the
                // assertion tied to THIS screen's hierarchy, not a stray global object.
                Transform cursor = button.transform;
                bool underMapContent = false;
                while (cursor != null)
                {
                    if (cursor.name == "mapContent") { underMapContent = true; break; }
                    cursor = cursor.parent;
                }
                Assert.IsTrue(underMapContent, $"row {i}'s button must sit inside mapContent");

                Transform row = FindByName(screen.transform, $"MissionRow_{screen.state.Rows[i].Mission.missionId}");
                Assert.IsNotNull(row, "the MissionRow_<id> object must exist by name under the screen");
                Assert.IsNotNull(row.GetComponent<Button>(), "the MissionRow_<id> object must carry the Button");
            }
        }

        [Test]
        public void CampaignMap_ScrollContentHeightFitsTheLayout()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            RectTransform content = screen.menu.listRoot;
            Assert.IsNotNull(content, "the scroll content must exist");

            var viewport = content.parent as RectTransform;
            Assert.IsNotNull(viewport, "the content must sit inside a viewport rect");
            var scroll = viewport.GetComponent<ScrollRect>();
            Assert.IsNotNull(scroll, "the viewport must carry the ScrollRect");
            Assert.AreSame(content, scroll.content, "ScrollRect.content must be the list root");

            // Top-pivot, top-stretch, anchored to zero: the canonical scroll-content shape — the
            // content's top edge pins to the viewport's top edge so the first band renders in view.
            Assert.AreEqual(1f, content.anchorMin.y, "content must be top-anchored");
            Assert.AreEqual(1f, content.anchorMax.y, "content must be top-anchored");
            Assert.AreEqual(1f, content.pivot.y, "content pivot must sit on its top edge");
            Assert.AreEqual(0f, content.anchoredPosition.y, 0.01f,
                "a freshly built map must open with the content's top at the viewport top");

            // ContentHeight is expressed in viewport-width units, so the scroll content's pixel
            // height must cover units × the measured viewport width — 1032 is the screen's
            // documented fallback when layout has not run (batch/EditMode).
            Canvas.ForceUpdateCanvases();
            float width = viewport.rect.width > 1f ? viewport.rect.width : 1032f;
            var nodes = CampaignMapLayout.Build(TestWorld.Database, SaveSystem.ReadFromDisk());
            float needed = CampaignMapLayout.ContentHeight(nodes) * width;
            Assert.GreaterOrEqual(content.sizeDelta.y, needed,
                "the scroll content must be tall enough to hold every node the layout placed");

            Transform mapContent = FindByName(screen.transform, "mapContent");
            Assert.IsNotNull(mapContent, "the map surface must exist inside the scroll content");
            Assert.AreEqual(content.sizeDelta.y, ((RectTransform)mapContent).sizeDelta.y, 0.01f,
                "the map surface must fill the whole scroll content");

            // Nodes anchor by fraction horizontally — a pixel x would bake in whatever width the
            // canvas measured at author time and clip off-screen on a differently-sized viewport.
            for (int i = 0; i < nodes.Count && i < screen.state.Rows.Count; i++)
            {
                var rowRect = screen.state.Rows[i].Button.GetComponent<RectTransform>();
                Assert.AreEqual(nodes[i].Anchor.x, rowRect.anchorMin.x, 0.001f,
                    $"row {i} must anchor its x at the layout fraction");
                Assert.AreEqual(nodes[i].Anchor.x, rowRect.anchorMax.x, 0.001f,
                    $"row {i} must anchor its x at the layout fraction");
            }
        }

        [Test]
        public void CampaignMap_EveryRowHasAClickSurface()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            foreach (CampaignMapScreen.MissionRow row in screen.state.Rows)
            {
                Button button = row.Button;
                Assert.IsNotNull(button);
                RectTransform rowRect = button.GetComponent<RectTransform>();

                // The Button's own GameObject must own no Graphic — Selectable.Awake() assigns
                // any same-object Graphic as targetGraphic, resurrecting a tintable plaque.
                Assert.IsNull(button.GetComponent<Graphic>(),
                    $"{row.Mission.missionId}: the Button object must carry no Graphic");

                bool covered = false;
                foreach (Graphic graphic in button.GetComponentsInChildren<Graphic>())
                {
                    if (!graphic.raycastTarget) continue;
                    Rect rect = graphic.rectTransform.rect;
                    if (rect.width >= rowRect.rect.width - 0.01f && rect.height >= rowRect.rect.height - 0.01f)
                    {
                        covered = true;
                        break;
                    }
                }
                Assert.IsTrue(covered, $"{row.Mission.missionId}: a full-rect raycast catcher must cover the row");
            }
        }

        [Test]
        public void CampaignMap_PlayIsArmedOnOpenAndSelectsOnClick()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Assert.AreEqual(0, screen.state.SelectedIndex,
                "the map must arm Play with a mission on open — a dead Play reads as a dead button");

            screen.state.Rows[0].Button.onClick.Invoke();
            Assert.AreEqual(0, screen.state.SelectedIndex, "the row button must route taps to SelectRow");

            screen.PlaySelected();
            Assert.AreEqual(TestWorld.Database.missions[0].missionId, CampaignManager.PendingMissionId,
                "Play on a freshly opened map must park the armed mission for battle");
        }

        [Test]
        public void CampaignMap_TitleGateShowsOncePerLaunch()
        {
            CampaignMapScreen screen = CreateScreen<CampaignMapScreen>();
            screen.Build(TestWorld.Database);

            Transform gate = FindByName(screen.transform, "TitleGate");
            Assert.IsNotNull(gate, "the first build of a launch must present the title gate");

            var begin = gate.Find("BeginButton")?.GetComponent<Button>();
            Assert.IsNotNull(begin, "the gate must carry a Begin button");
            Assert.IsTrue(begin.interactable, "the Begin button must be clickable");
            begin.onClick.Invoke();

            Assert.IsFalse(gate.gameObject.activeSelf, "Begin must dismiss the gate");
            Assert.IsTrue(CampaignMapScreen.s_titleShown, "Begin marks the title as shown for the rest of the launch");

            screen.Build(TestWorld.Database);
            Transform gateAgain = FindByName(screen.transform, "TitleGate");
            Assert.IsTrue(gateAgain == null || !gateAgain.gameObject.activeSelf,
                "the title gate must not reappear within the same launch");
        }

        private static Transform FindByName(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                Transform child = root.GetChild(i);
                if (child.name == name) return child;
                Transform found = FindByName(child, name);
                if (found != null) return found;
            }
            return null;
        }

        [Test]
        public void KingdomScreen_BuyButtonIsDisabledWithoutTheCrowns()
        {
            KingdomScreen screen = CreateScreen<KingdomScreen>();
            screen.Build(TestWorld.Database);

            Assert.IsNotEmpty(screen.Rows, "the kingdom must show one row per upgrade");
            Assert.IsFalse(screen.Rows[0].BuyButton.interactable, "nothing is affordable on a fresh, crownless save");

            SaveSystem.WriteToDisk(new SaveData { crowns = 1000 });
            screen.Refresh();

            Assert.IsTrue(screen.Rows[0].BuyButton.interactable, "enough crowns must enable the buy button");
        }

        [Test]
        public void SkirmishScreen_StartParksTheResolvedSkirmishWithoutConsultingUnlocks()
        {
            SkirmishScreen screen = CreateScreen<SkirmishScreen>();
            screen.Build(TestWorld.Database);

            screen.StartSkirmish();

            Assert.AreEqual("skirmish_01", CampaignManager.PendingMissionId,
                "a fresh save must still be able to start the default skirmish");
        }

        private static int CountInteractable(CampaignMapScreen screen)
        {
            int count = 0;
            for (int i = 0; i < screen.state.Rows.Count; i++)
            {
                if (screen.state.Rows[i].Button != null && screen.state.Rows[i].Button.interactable) count++;
            }

            return count;
        }

        private static T CreateScreen<T>() where T : Component
        {
            GameObject go = TestWorld.Track(new GameObject($"{typeof(T).Name}Screen"));
            return go.AddComponent<T>();
        }

        /// <summary>Twelve chained missions in six territories, three skirmishes and five upgrades.</summary>
        private void AppendContent(GameDatabase database)
        {
            string[] territories =
            {
                "Greenwood Valley", "Stonebridge", "The Iron Hills",
                "Sunfall Plains", "Ashen Frontier", "The Crownlands"
            };

            var missions = new List<MissionDefinition>(12);
            for (int i = 0; i < 12; i++)
            {
                var mission = ScriptableObject.CreateInstance<MissionDefinition>();
                mission.missionId = $"mission_{i + 1:00}";
                mission.title = $"{territories[i / 2]}: Objective {i + 1}";
                mission.unlockRequirement = i == 0 ? string.Empty : $"mission_{i:00}";
                _content.Add(mission);
                missions.Add(mission);
            }

            var skirmishes = new List<MissionDefinition>(3);
            string[] maps = { "Greenwood", "Stonebridge", "Iron Hills" };
            for (int i = 0; i < 3; i++)
            {
                var skirmish = ScriptableObject.CreateInstance<MissionDefinition>();
                skirmish.missionId = $"skirmish_{i + 1:00}";
                skirmish.title = $"Skirmish: {maps[i]}";
                skirmish.isSkirmish = true;
                skirmish.unlockRequirement = string.Empty;
                skirmish.crownReward = i + 1;
                _content.Add(skirmish);
                skirmishes.Add(skirmish);
            }

            var upgrades = new List<KingdomUpgradeDefinition>(5);
            int[] costs = { 8, 14, 22, 35, 18 };
            for (int i = 0; i < 5; i++)
            {
                var upgrade = ScriptableObject.CreateInstance<KingdomUpgradeDefinition>();
                upgrade.upgradeId = $"upg_{i}";
                upgrade.title = $"Upgrade {i}";
                upgrade.crownCost = costs[i];
                _content.Add(upgrade);
                upgrades.Add(upgrade);
            }

            database.missions = missions;
            database.skirmishes = skirmishes;
            database.upgrades = upgrades;
            database.InvalidateCaches();
        }
    }
}
