namespace Shackles.JobObjects;

/// <summary>Access rights that may be requested when opening a named job.</summary>
[Flags]
public enum JobAccessRights : uint
{
    None = 0,
    AssignProcess = 0x0001,
    SetAttributes = 0x0002,
    Query = 0x0004,
    Terminate = 0x0008,
    SetSecurityAttributes = 0x0010,
    Impersonate = 0x0020,
    Delete = 0x00010000,
    ReadControl = 0x00020000,
    WriteDac = 0x00040000,
    WriteOwner = 0x00080000,
    Synchronize = 0x00100000,

    /// <summary>The least-privilege set needed by Shackles to query, configure, and assign processes.</summary>
    Manage = AssignProcess | SetAttributes | Query,

    FullControl = 0x001F003F
}
