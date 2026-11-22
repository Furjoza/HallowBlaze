using System;
using System.Linq;
using HallowBlaze.Core.Generation;
using HallowBlaze.Core.Generation.Pipeline;
using HallowBlaze.Core.State;
using HallowBlaze.Presentation.Runtime;
using Newtonsoft.Json.Linq;

namespace HallowBlaze.Editor.BoardAuthoring
{
    /// <summary>Captures the production generator's accepted blueprint into an independent shared draft, never a scene scrape.</summary>
    public static class BoardGenerationAdapter
    {
        /// <summary>Generates the selected day preset using the real bounded pipeline; equal inputs reproduce the exact snapshot.</summary>
        /// <param name="seed">Explicit root seed, also retained in the generated address.</param>
        /// <param name="day">Positive production difficulty day; does not advance a real run.</param>
        /// <param name="templateId">New draft identity, independent of the generator configuration.</param>
        /// <param name="configurationId">New snapshot configuration identity; generation provenance is not a reroll instruction.</param>
        public static BoardTemplateDocument Generate(int seed, int day, string templateId, string configurationId)
        {
            if (day < 1 || day > 30) throw new ArgumentOutOfRangeException(nameof(day));
            var address = new BoardAddress("authoring-generation", "leg.1", "authoring.route", "authoring.segment", day,
                OrdinaryBoardGenerator.ConfigurationId, BoardGenerationPipeline.OrdinaryConfigurationVersion, seed);
            var config = OrdinaryBoardContent.CreateConfiguration(address);
            var result = BoardGenerationPipeline.GenerateOrdinary(new BoardGenerationRequest(address, config, OrdinaryBoardContent.CreateCatalog(), seed));
            if (!result.IsSuccess) throw new InvalidOperationException(result.ToString());
            var document = BoardTemplateDocument.NewBoard(templateId, configurationId, OrdinaryBoardContent.CreateAuthoringCatalog());
            document.Edit(data =>
            {
                var blueprint = result.Blueprint;
                var variant = (JObject)data["variants"][0];
                variant["entities"] = new JArray(blueprint.Entities.Select(entity => new JObject
                {
                    ["id"] = entity.Id.Value, ["layer"] = entity.Definition.Layer.ToString(), ["contentId"] = entity.Definition.ContentId,
                    ["x"] = entity.Position.X, ["y"] = entity.Position.Y
                }));
                variant["items"] = new JArray(blueprint.Items.Select(item => new JObject
                {
                    ["instanceId"] = item.InstanceId, ["entityId"] = item.EntityId.Value, ["itemDefinitionId"] = item.ItemDefinitionId,
                    ["resourceFamilyId"] = item.ResourceFamilyId, ["sourceId"] = item.SourceId == null ? JValue.CreateNull() : new JValue(item.SourceId),
                    ["initiallyObservable"] = item.InitiallyObservable
                }));
                data["configuration"]["allowedContentIds"] = new JArray(config.AllowedContentIds);
                data["configuration"]["resourceBudgets"] = new JArray(config.ResourceBudgets.Select(budget => new JObject
                {
                    ["resourceFamilyId"] = budget.ResourceFamilyId, ["itemCount"] = new JObject { ["minimum"] = 0, ["maximum"] = 64 }
                }));
                data["outcomes"] = new JArray(blueprint.Entities.Where(entity => entity.Definition.Traits.IsExit).Select(entity => new JObject
                { ["id"] = "exit." + entity.Id.Value, ["name"] = "Exit", ["exitEntityId"] = entity.Id.Value, ["optional"] = false }));
            });
            return document;
        }
    }
}