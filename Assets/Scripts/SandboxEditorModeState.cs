using System;

/// <summary>High-level authoring mode for the sandbox shell (Phase D2 adds <see cref="Draw"/>).</summary>
public enum SandboxEditorSessionMode
{
    /// <summary>Place assets, drawer, inspector, and existing editor tools (default).</summary>
    Edit = 0,

    /// <summary>Stylus / line drawing focus; main editor strip swaps to the draw bar.</summary>
    Draw = 1
}

/// <summary>
/// Shared session mode for the editor scene (Edit vs Draw). Simulation always runs in Edit semantics for the shell.
/// </summary>
public static class SandboxEditorModeState
{
    public static SandboxEditorSessionMode Current { get; private set; } = SandboxEditorSessionMode.Edit;

    /// <summary>Fired after <see cref="Current"/> changes (e.g. when Draw mode is added in D2).</summary>
    public static event Action<SandboxEditorSessionMode> ModeChanged;

    /// <summary>Forces <see cref="Edit"/> when the sandbox scene starts (covers domain-reload-off edge cases).</summary>
    public static void ResetToEditForPlaySession()
    {
        if (Current == SandboxEditorSessionMode.Edit)
            return;

        Current = SandboxEditorSessionMode.Edit;
        ModeChanged?.Invoke(Current);
    }

    /// <summary>Shell-only transitions (D2+). D1 keeps a single mode; calling with <see cref="SandboxEditorSessionMode.Edit"/> is a no-op if already Edit.</summary>
    public static void SetSessionMode(SandboxEditorSessionMode mode)
    {
        if (mode == Current)
            return;

        Current = mode;
        ModeChanged?.Invoke(Current);
    }

    public static string GetDisplayLabel(SandboxEditorSessionMode mode) => mode switch
    {
        SandboxEditorSessionMode.Edit => "Edit",
        SandboxEditorSessionMode.Draw => "Draw",
        _ => mode.ToString()
    };
}
