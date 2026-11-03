using Filee.Core.Formats;
using Filee.Core.Presets;
using Xunit;

namespace Filee.Core.Tests;

public class ResizerPresetTests
{
    [Fact]
    public void Resizer_presets_exist_and_include_compress_group_and_sizes()
    {
        var presets = BuiltInData.ResizerPresets();
        Assert.Equal(8, presets.Count);

        var compress = presets.Single(p => p.Id == "size-compress");
        Assert.Equal(BuiltInData.SameAsSource, compress.TargetFormat);
        Assert.Equal(75, compress.Image.Quality);
        Assert.True(compress.Image.MaxCompression);
        Assert.Equal(PdfCompression.Ebook, compress.Pdf.Compression);
        Assert.True(compress.Output.OnlyIfSmaller);
        Assert.Equal("{name}_compressed", compress.Output.FileNamePattern);

        var group = presets.Single(p => p.Id == "resizer-group");
        Assert.Equal(PresetKind.Group, group.Kind);
        Assert.Equal(Preset.GroupTarget, group.TargetFormat);
        Assert.Equal(["size-compress", "size-1mb", "size-2mb", "size-5mb", "size-10mb", "size-15mb", "size-25mb"], group.SubPresetIds);
    }

    [Theory]
    [InlineData("size-1mb", 1)]
    [InlineData("size-2mb", 2)]
    [InlineData("size-5mb", 5)]
    [InlineData("size-10mb", 10)]
    [InlineData("size-15mb", 15)]
    [InlineData("size-25mb", 25)]
    public void Size_presets_have_correct_limits_for_both_image_and_pdf(string id, double expectedMb)
    {
        var preset = BuiltInData.ResizerPresets().Single(p => p.Id == id);
        Assert.Equal(BuiltInData.SameAsSource, preset.TargetFormat);
        Assert.Equal(expectedMb, preset.Image.MaxSizeMb);
        Assert.Equal(expectedMb, preset.Pdf.MaxSizeMb);

        var expectedBytes = (long)(expectedMb * 1_000_000);
        Assert.Equal(expectedBytes, SizeLimit.For(preset, "test.jpg"));
        Assert.Equal(expectedBytes, SizeLimit.For(preset, "test.png"));
        Assert.Equal(expectedBytes, SizeLimit.For(preset, "test.pdf"));
    }

    [Fact]
    public void Images_and_pdf_back_faces_offer_resizer_group_without_standalone_compress()
    {
        var profiles = BuiltInData.CreateProfiles();

        var images = profiles.Single(p => p.Id == "images");
        Assert.NotNull(images.BackPresetIds);
        Assert.Contains("resizer-group", images.BackPresetIds);
        Assert.DoesNotContain("compress-image", images.BackPresetIds);
        Assert.Equal(8, images.BackPresetIds.Count);

        var pdf = profiles.Single(p => p.Id == "pdf");
        Assert.NotNull(pdf.BackPresetIds);
        Assert.Contains("resizer-group", pdf.BackPresetIds);
        Assert.DoesNotContain("compress-pdf", pdf.BackPresetIds);
        Assert.InRange(pdf.BackPresetIds.Count, 1, 8);
    }
}
