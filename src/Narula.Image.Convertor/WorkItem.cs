namespace Narula.Image.Convertor;

/// <summary>One file to convert: where it came from, where it goes, and how to name it in reports.</summary>
internal readonly record struct WorkItem(string SourcePath, string DestinationPath, string RelativePath);
