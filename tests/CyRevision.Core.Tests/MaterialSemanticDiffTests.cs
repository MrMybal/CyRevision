using CyRevision.Plugin.Unreal;

namespace CyRevision.Core.Tests;

public sealed class MaterialSemanticDiffTests
{
    [Fact]
    public void ReportsParametersExpressionsConnectionsAndSettings()
    {
        MaterialSemanticDiffResult? result = MaterialSemanticDiff.Compare(
            Manifest(parameterValue: "0.2", includeTexture: true, connectTexture: true, twoSided: false),
            Manifest(parameterValue: "0.8", includeTexture: false, connectTexture: false, twoSided: true),
            "Content/M_Demo.uasset");

        Assert.NotNull(result);
        Assert.Contains("Material semantic diff", result.Text);
        Assert.Contains("Parameters: +0 / -0 / ~1", result.Text);
        Assert.Contains("Expressions: +0 / -1", result.Text);
        Assert.Contains("Connections: +0 / -1", result.Text);
        Assert.NotEqual("0", result.Metadata["Semantic changes"]);
    }

    [Fact]
    public void EquivalentManifestsProduceAnExplicitSemanticResult()
    {
        string manifest = Manifest("0.5", true, true, false);

        MaterialSemanticDiffResult? result = MaterialSemanticDiff.Compare(
            manifest,
            manifest,
            "Content/M_Demo.uasset");

        Assert.NotNull(result);
        Assert.Equal("Material graph is semantically equivalent", result.Summary);
        Assert.Contains("No semantic Material change was detected.", result.Text);
        Assert.Equal("0", result.Metadata["Semantic changes"]);
    }

    [Fact]
    public void NonMaterialManifestIsNotClaimed()
    {
        MaterialSemanticDiffResult? result = MaterialSemanticDiff.Compare(
            "{\"assetKind\":\"Blueprint\"}",
            "{\"assetKind\":\"Blueprint\"}",
            "Content/BP_Demo.uasset");

        Assert.Null(result);
    }

    private static string Manifest(
        string parameterValue,
        bool includeTexture,
        bool connectTexture,
        bool twoSided)
    {
        string texture = includeTexture
            ? $$"""
              ,{
                "key":"texture-node","guid":"texture-node","name":"TextureSample","class":"/Script/Engine.MaterialExpressionTextureSample",
                "description":"Albedo","x":-300,"y":0,
                "properties":[{"name":"SamplerType","value":"SAMPLERTYPE_Color"}],
                "references":[{{(connectTexture ? "\"multiply-node\"" : string.Empty)}}]
              }
              """
            : string.Empty;
        string multiplyReferences = connectTexture ? "\"texture-node\"" : string.Empty;
        return $$"""
          {
            "schemaVersion":3,
            "assetKind":"Material",
            "material":{
              "class":"/Script/Engine.Material",
              "blendMode":0,
              "shadingModels":"Default Lit",
              "twoSided":{{twoSided.ToString().ToLowerInvariant()}},
              "settings":[{"name":"OpacityMaskClipValue","value":"0.333"}],
              "parameters":[
                {"key":"Scalar:Roughness:0:-1","type":"Scalar","name":"Roughness","value":"{{parameterValue}}","overridden":true}
              ],
              "expressions":[
                {
                  "key":"multiply-node","guid":"multiply-node","name":"Multiply","class":"/Script/Engine.MaterialExpressionMultiply",
                  "description":"","x":0,"y":0,
                  "properties":[{"name":"ConstA","value":"1.0"}],
                  "references":[{{multiplyReferences}}]
                }
                {{texture}}
              ],
              "expressionsTruncated":false
            }
          }
          """;
    }
}
