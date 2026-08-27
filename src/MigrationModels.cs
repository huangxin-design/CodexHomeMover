using System;
using System.Collections.Generic;
using System.Runtime.Serialization;

namespace CodexHomeMover
{
    internal enum MigrationStage
    {
        Idle,
        Preflight,
        WaitingForCodex,
        Copying,
        Reconciling,
        Verifying,
        CheckingDatabases,
        SecuringPermissions,
        Switching,
        Completed,
        RollingBack,
        CleaningUp,
        Failed
    }

    internal sealed class MigrationProgress
    {
        public MigrationStage Stage { get; set; }
        public int Percent { get; set; }
        public string Message { get; set; }
        public long ProcessedBytes { get; set; }
        public long TotalBytes { get; set; }
        public int ProcessedFiles { get; set; }
        public int TotalFiles { get; set; }
    }

    internal sealed class MigrationOptions
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public bool VerifySha256 { get; set; }
        public bool AllowExistingDestination { get; set; }
        public bool IgnoreRunningProcesses { get; set; }
        public bool RequireAdministrator { get; set; }
        public string RecordPathOverride { get; set; }

        public MigrationOptions()
        {
            VerifySha256 = true;
            RequireAdministrator = true;
        }
    }

    internal sealed class PreflightResult
    {
        public string SourcePath { get; set; }
        public string DestinationPath { get; set; }
        public long SourceBytes { get; set; }
        public long RequiredAdditionalBytes { get; set; }
        public long DestinationFreeBytes { get; set; }
        public int FileCount { get; set; }
        public int DirectoryCount { get; set; }
        public int JunctionCount { get; set; }
        public bool DestinationHasData { get; set; }
        public IList<string> Warnings { get; set; }

        public PreflightResult()
        {
            Warnings = new List<string>();
        }
    }

    [DataContract]
    internal sealed class MigrationRecord
    {
        [DataMember(Order = 1)]
        public int FormatVersion { get; set; }

        [DataMember(Order = 2)]
        public string SourcePath { get; set; }

        [DataMember(Order = 3)]
        public string DestinationPath { get; set; }

        [DataMember(Order = 4)]
        public string BackupPath { get; set; }

        [DataMember(Order = 5)]
        public string QuarantinePath { get; set; }

        [DataMember(Order = 6)]
        public DateTime CompletedUtc { get; set; }

        [DataMember(Order = 7)]
        public bool BackupDeleted { get; set; }
    }

    internal sealed class FileSnapshot
    {
        public string FullPath { get; set; }
        public string RelativePath { get; set; }
        public long Length { get; set; }
        public DateTime LastWriteUtc { get; set; }
        public System.IO.FileAttributes Attributes { get; set; }
    }

    internal sealed class JunctionSnapshot
    {
        public string FullPath { get; set; }
        public string RelativePath { get; set; }
        public string TargetPath { get; set; }
    }

    internal sealed class DirectorySnapshot
    {
        public string RootPath { get; set; }
        public IList<FileSnapshot> Files { get; set; }
        public IList<string> Directories { get; set; }
        public IList<JunctionSnapshot> Junctions { get; set; }
        public long TotalBytes { get; set; }

        public DirectorySnapshot()
        {
            Files = new List<FileSnapshot>();
            Directories = new List<string>();
            Junctions = new List<JunctionSnapshot>();
        }
    }
}
