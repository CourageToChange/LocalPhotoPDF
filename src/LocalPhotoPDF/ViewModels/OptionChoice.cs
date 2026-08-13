namespace LocalPhotoPDF.ViewModels;

internal sealed record OptionChoice<T>(T Value, string Label, string Description)
    where T : struct, Enum;
