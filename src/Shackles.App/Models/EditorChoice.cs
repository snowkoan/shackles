namespace Shackles.App.Models;

internal sealed record EditorChoice<T>(T Value, string Label, string Description) where T : struct, Enum;
