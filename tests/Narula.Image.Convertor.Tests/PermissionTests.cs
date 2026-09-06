using Narula.Image.Convertor;

namespace ImageConvertor.Tests;

public class PermissionTests
{
    [Fact]
    public void An_access_exception_is_a_refusal()
    {
        Assert.True(MagickConverter.IsPermissionRefusal(new UnauthorizedAccessException()));
    }

    [Theory]
    [InlineData("unable to open image 'x.png': Permission denied")]
    [InlineData("UnableToOpenBlob `out.tmp' @ error/blob.c/OpenBlob/3596: Access is denied")]
    public void A_refusal_is_recognised_from_the_message(string message)
    {
        // ImageMagick reports the refusal in the text rather than the exception type.
        Assert.True(MagickConverter.IsPermissionRefusal(new IOException(message)));
    }

    [Theory]
    [InlineData("corrupt image")]
    [InlineData("image type not supported")]
    [InlineData("no decode delegate for this image format")]
    public void An_ordinary_image_failure_is_not_a_refusal(string message)
    {
        Assert.False(MagickConverter.IsPermissionRefusal(new IOException(message)));
    }
}
