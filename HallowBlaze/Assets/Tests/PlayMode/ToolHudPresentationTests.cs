using System;
using System.IO;
using System.Linq;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace HallowBlaze.Tests.PlayMode
{
    /// <summary>Verifies read-only equipment presentation and authored placeholders without gameplay or save mutations.</summary>
    public sealed class ToolHudPresentationTests
    {
        /// <summary>Both equipment slots are generic and reads never consume charges or mutate reusable definitions.</summary>
        [Test]
        public void HudReadsCurrentEquipmentAndPreservesUnknownInstances()
        {
            var run = new RunState("hud-tools", 52, new RunStateConfiguration(100, 100, 0, "forest.start"));
            var definitions = ToolContentCatalog.Load();
            ToolInstanceState first = run.CreateToolInstance(definitions["axe.basic"]);
            ToolInstanceState second = run.CreateToolInstance(definitions["axe.basic"]);
            run.EquipTool(0, first);
            run.EquipTool(1, second);
            using (var hud = new ToolHudPresenter(run, null, new Vector2(640, 480), definitions))
            {
                Assert.That(hud.GetSlotText(0), Is.EqualTo("Axe  3"));
                Assert.That(hud.GetSlotText(1), Is.EqualTo("Axe  3"));
                Assert.That(hud.Root.GetComponentsInChildren<Button>(), Is.Empty);
                Assert.That(run.TrySpendToolCharge(0, first.InstanceId), Is.True);
                hud.Refresh();
                Assert.That(hud.GetSlotText(0), Is.EqualTo("Axe  2"));
                Assert.That(hud.GetSlotText(1), Is.EqualTo("Axe  3"));
                ToolInstanceState unknown = run.CreateToolInstance(new ToolDefinition("missing.content", "Missing", 1));
                run.ReplaceTool(1, unknown);
                run.TrySpendToolCharge(1, unknown.InstanceId);
                hud.Refresh();
                hud.Refresh();
                Assert.That(hud.GetSlotText(1), Is.EqualTo("Unknown tool  0"));
                Assert.That(run.ToolSlots[1].Instance.DefinitionId, Is.EqualTo("missing.content"));
                run.RemoveTool(0);
                hud.Refresh();
                Assert.That(hud.GetSlotText(0), Is.EqualTo("Empty"));
                Assert.That(definitions["axe.basic"].MaximumCharges, Is.EqualTo(3));
                Assert.That(first.Charges, Is.EqualTo(3));
                Assert.That(run.Food, Is.EqualTo(100));
                Assert.That(run.Health, Is.EqualTo(100));
            }
        }

        /// <summary>Placeholder resources are reusable immutable data and invalid content never publishes definitions.</summary>
        [Test]
        public void PlaceholderContentIsValidatedAndDoesNotAllocateEquipment()
        {
            var definitions = ToolContentCatalog.Load();
            Assert.That(definitions.Keys, Is.EquivalentTo(new[] { "axe.basic", "shovel.basic" }));
            Assert.That(definitions.Values.All(definition => definition.MaximumCharges > 0), Is.True);
            Assert.Throws<ArgumentException>(() => ToolContentCatalog.Read("{}"));
            Assert.Throws<ArgumentOutOfRangeException>(() => ToolContentCatalog.Read(
                "{\"tools\":[{\"id\":\"axe.basic\",\"displayName\":\"Axe\",\"maximumCharges\":-1}]}"));
            Assert.Throws<ArgumentException>(() => ToolContentCatalog.Read(
                "{\"tools\":[{\"id\":\"same\",\"displayName\":\"Axe\",\"maximumCharges\":3}," +
                "{\"id\":\"same\",\"displayName\":\"Shovel\",\"maximumCharges\":3}]}"));
        }

        /// <summary>Production clearing content opts in only soft wood and preserves an explicit cheaper/quieter manual alternative.</summary>
        [Test]
        public void AxeClearingContentHasOneStableDefinitionAndValidatedManualAlternative()
        {
            var rules = AxeContent.LoadRules();
            Assert.That(rules.ObstacleContentIds, Is.EqualTo(new[] { "legacy.wall" }));
            Assert.That(rules.RequiredWork, Is.EqualTo(3));
            Assert.That(rules.AxeNoiseStrength, Is.GreaterThan(rules.ManualNoiseStrength));
            Assert.That(rules.NoiseLifetimeTurns, Is.EqualTo(2));
            Assert.That(ToolContentCatalog.Load().ContainsKey("axe.basic"), Is.True);
            Assert.Throws<ArgumentOutOfRangeException>(() => AxeContent.ReadRules(
                "{\"obstacleContentIds\":[\"wood\"],\"requiredWork\":1,\"manualNoiseStrength\":1,\"axeNoiseStrength\":6,\"noiseLifetimeTurns\":2}"));
            Assert.Throws<ArgumentNullException>(() => AxeContent.ReadRules("{}"));
        }

        /// <summary>Both populated slot labels render in a bounded top band at desktop and narrow portrait dimensions.</summary>
        [TestCase(1280, 720)]
        [TestCase(320, 640)]
        public void TwoSlotHudRendersReadableLabelsAtDesktopAndPortraitSizes(int width, int height)
        {
            Assert.That(SystemInfo.graphicsDeviceType, Is.Not.EqualTo(UnityEngine.Rendering.GraphicsDeviceType.Null));
            var run = new RunState("hud-render", 52, new RunStateConfiguration(100, 100, 0, "forest.start"));
            var definitions = ToolContentCatalog.Load();
            run.EquipTool(0, run.CreateToolInstance(definitions["axe.basic"]));
            run.EquipTool(1, run.CreateToolInstance(definitions["shovel.basic"]));
            var owner = new GameObject("Equipment render camera");
            Camera camera = owner.AddComponent<Camera>();
            camera.enabled = false;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            var target = new RenderTexture(width, height, 24);
            var pixels = new Texture2D(width, height, TextureFormat.RGB24, false);
            RenderTexture previous = RenderTexture.active;
            try
            {
                using (var hud = new ToolHudPresenter(run, null, new Vector2(width, height), definitions))
                {
                    Canvas canvas = hud.Root.GetComponent<Canvas>();
                    canvas.renderMode = RenderMode.ScreenSpaceCamera;
                    canvas.worldCamera = camera;
                    canvas.planeDistance = 1f;
                    camera.targetTexture = target;
                    Canvas.ForceUpdateCanvases();
                    foreach (Text text in hud.Root.GetComponentsInChildren<Text>())
                        Assert.That(text.preferredWidth, Is.LessThanOrEqualTo(text.rectTransform.rect.width), text.text);
                    camera.Render();
                    RenderTexture.active = target;
                    pixels.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                    pixels.Apply();
                    for (int slot = 0; slot < RunState.ToolSlotCount; slot++)
                    {
                        int bright = 0;
                        int start = Mathf.RoundToInt(hud.Bounds.xMin + slot * hud.Bounds.width / 2f);
                        int end = Mathf.RoundToInt(hud.Bounds.xMin + (slot + 1) * hud.Bounds.width / 2f);
                        for (int vertical = height - 60; vertical < height - 8; vertical++)
                            for (int horizontal = start; horizontal < end; horizontal++)
                            {
                                Color color = pixels.GetPixel(horizontal, vertical);
                                if (color.r > 0.6f && color.g > 0.6f && color.b > 0.6f) bright++;
                            }
                        Assert.That(bright, Is.GreaterThan(50), "Readable tool label pixels for slot " + slot);
                    }
                    string image = Path.Combine(Path.GetTempPath(), "HB-M52-HUD-" + width + "x" + height + ".png");
                    File.WriteAllBytes(image, pixels.EncodeToPNG());
                    TestContext.WriteLine("Equipment HUD evidence: " + image);
                    Assert.That(run.ToolSlots[0].RemainingUses, Is.EqualTo(3));
                    Assert.That(run.ToolSlots[1].RemainingUses, Is.EqualTo(3));
                }
            }
            finally
            {
                camera.targetTexture = null;
                RenderTexture.active = previous;
                UnityEngine.Object.DestroyImmediate(pixels);
                UnityEngine.Object.DestroyImmediate(target);
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }
    }
}