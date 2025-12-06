using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenFXC.Sem;
using Xunit;

namespace OpenFXC.Sem.Tests;

public class FxTechniqueTests
{
    [Fact]
    public void Pass_missing_pixel_shader_reports_diagnostic()
    {
        var source = @"
float4 main(float4 p : POSITION) : SV_Position { return p; }

technique T {
    pass P {
        VertexShader = compile vs_2_0 main();
    }
};";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL5005");
    }

    [Fact]
    public void Duplicate_pass_names_emit_diagnostic()
    {
        var source = @"
float4 main(float4 p : POSITION) : SV_Position { return p; }

technique T {
    pass P {
        VertexShader = compile vs_2_0 main();
        PixelShader = compile ps_2_0 main();
    }
    pass P {
        VertexShader = compile vs_2_0 main();
        PixelShader = compile ps_2_0 main();
    }
};";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL5003");
    }

    [Fact]
    public void Stage_profile_mismatch_reports_diagnostic()
    {
        var source = @"
float4 main(float4 p : POSITION) : SV_Position { return p; }

technique T {
    pass P {
        VertexShader = compile ps_2_0 main();
        PixelShader = compile ps_2_0 main();
    }
};";

        using var doc = JsonDocument.Parse(RunParseThenAnalyzeSource(source, "vs_2_0"));
        var diagnostics = doc.RootElement.GetProperty("diagnostics").EnumerateArray().ToList();

        Assert.Contains(diagnostics, d => d.GetProperty("id").GetString() == "HLSL5006");
    }

    private static string RunParseThenAnalyzeSource(string source, string profile)
    {
        var astJson = ParseHelper.BuildAstJson(source);
        var analyzer = new SemanticAnalyzer(profile, "main", astJson);
        var output = analyzer.Analyze();
        return JsonSerializer.Serialize(output, new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        });
    }
}
