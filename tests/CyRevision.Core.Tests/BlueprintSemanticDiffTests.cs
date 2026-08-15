using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class BlueprintSemanticDiffTests
{
    [Fact]
    public void ReportsNodesConnectionsAndVariableDefaults()
    {
        BlueprintSemanticDiffResult? result = BlueprintSemanticDiff.Compare(
            Manifest(
                variableDefault: "1.0",
                includeSecondNode: true,
                connectNodes: true),
            Manifest(
                variableDefault: "2.0",
                includeSecondNode: false,
                connectNodes: false),
            "Content/BP_Demo.uasset");

        Assert.NotNull(result);
        Assert.Contains("Blueprint semantic diff", result.Text);
        Assert.Contains("Nodes: +0 / -1", result.Text);
        Assert.Contains("Connections: +0 / -1", result.Text);
        Assert.Contains("Variables: +0 / -0 / ~1", result.Text);
        Assert.Equal("1", result.Metadata["Nodes removed"]);
        Assert.Equal("1", result.Metadata["Connections removed"]);
    }

    [Fact]
    public void EquivalentManifestsProduceAnExplicitSemanticResult()
    {
        string manifest = Manifest("1.0", true, true);

        BlueprintSemanticDiffResult? result = BlueprintSemanticDiff.Compare(
            manifest,
            manifest,
            "Content/BP_Demo.uasset");

        Assert.NotNull(result);
        Assert.Equal("Blueprint graphs are semantically equivalent", result.Summary);
        Assert.Contains("No semantic Blueprint change was detected.", result.Text);
        Assert.Equal("0", result.Metadata["Semantic changes"]);
    }

    [Fact]
    public void NonBlueprintManifestIsNotClaimed()
    {
        BlueprintSemanticDiffResult? result = BlueprintSemanticDiff.Compare(
            "{\"assetKind\":\"Static mesh\"}",
            "{\"assetKind\":\"Static mesh\"}",
            "Content/SM_Demo.uasset");

        Assert.Null(result);
    }

    private static string Manifest(string variableDefault, bool includeSecondNode, bool connectNodes)
    {
        string nodeB = includeSecondNode
            ? """
              ,{
                "key":"node-b","guid":"node-b","name":"CallFunction","class":"K2Node_CallFunction",
                "title":"Print String","x":450,"y":100,
                "pins":[
                  {"key":"Input:execute:0","name":"execute","direction":"Input","type":"exec","default":"","links":["node-a:Output:then"]}
                ]
              }
              """
            : string.Empty;
        string links = connectNodes ? "\"node-b:Input:execute\"" : string.Empty;
        return $$"""
          {
            "schemaVersion":2,
            "assetKind":"Blueprint",
            "blueprint":{
              "parentClass":"/Script/Engine.Actor",
              "variables":[
                {"key":"var-health","guid":"var-health","name":"Health","friendlyName":"Health","type":"real:float","default":"{{variableDefault}}","category":"Default","repNotify":"None"}
              ],
              "graphs":[
                {
                  "key":"Event:EventGraph","name":"EventGraph","kind":"Event",
                  "nodes":[
                    {
                      "key":"node-a","guid":"node-a","name":"BeginPlay","class":"K2Node_Event",
                      "title":"Event BeginPlay","x":100,"y":100,
                      "pins":[
                        {"key":"Output:then:0","name":"then","direction":"Output","type":"exec","default":"","links":[{{links}}]}
                      ]
                    }
                    {{nodeB}}
                  ]
                }
              ],
              "nodesTruncated":false,
              "pinsTruncated":false
            }
          }
          """;
    }
}
