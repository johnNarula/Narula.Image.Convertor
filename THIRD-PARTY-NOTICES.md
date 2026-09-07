# Third-party notices

img2img is MIT licensed with additional terms — see [LICENSE](LICENSE). It bundles the
components below, each under its own licence. **Keep this file, and the `licenses/` folder it
refers to, with any copy you distribute**: two of these licences require it.

The published executables are single-file, so these components are inside `img2img.exe`,
`img2imgUI.exe` and `img2imgMcp.exe` rather than sitting beside them. The installer ships this
file and the licence texts into the install folder for exactly that reason.

| Component | Licence | Full text |
|---|---|---|
| [Magick.NET](https://github.com/dlemstra/Magick.NET) — Dirk Lemstra | Apache-2.0 | [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) |
| [ImageMagick](https://imagemagick.org) — ImageMagick Studio LLC, bundled as native libraries inside Magick.NET | ImageMagick Licence (Apache-2.0 derived) | [licenses/Magick.NET-and-ImageMagick-NOTICE.txt](licenses/Magick.NET-and-ImageMagick-NOTICE.txt) |
| [Avalonia](https://avaloniaui.net) — AvaloniaUI OÜ (window only) | MIT | [licenses/Avalonia-MIT.txt](licenses/Avalonia-MIT.txt) |
| [ModelContextProtocol C# SDK](https://github.com/modelcontextprotocol/csharp-sdk) (MCP server only) | Apache-2.0 | [licenses/Apache-2.0.txt](licenses/Apache-2.0.txt) |
| [.NET runtime and libraries](https://github.com/dotnet/runtime) — .NET Foundation and Contributors | MIT | [licenses/dotnet-MIT.txt](licenses/dotnet-MIT.txt) |

## What each one actually requires of you

**Apache-2.0** (Magick.NET, MCP SDK): keep the copyright, patent, trademark and attribution
notices, and include a copy of the licence with any distribution. That copy is
`licenses/Apache-2.0.txt`.

**The ImageMagick licence**: include a copy of it with any redistribution that contains
ImageMagick, and give clear attribution to ImageMagick Studio LLC. Do not use ImageMagick's
marks in a way that implies it endorses this software or that you wrote it. That copy is
`licenses/Magick.NET-and-ImageMagick-NOTICE.txt`, which is the notice file Magick.NET itself
ships, reproduced unchanged.

**MIT** (Avalonia, .NET): include the copyright notice and the permission notice. Those are the
two files above.

None of these licences is copyleft. Nothing here obliges you to publish your own source, and
nothing here constrains img2img's own MIT licence.

## Why one of those files is 8,000 lines

ImageMagick reads and writes many formats through other libraries compiled into it —
libheif 1.23.2, libjxl 0.12.0, libwebp 1.6.0, libpng 1.6.58, libjpeg-turbo 3.2.0,
libtiff 4.7.2 and around thirty more. Each carries its own licence and notice, and Magick.NET
collects all of them into the single `Notice.txt` reproduced here unchanged. Shipping that one
file is what covers the whole delegate chain.
