namespace NLQueryApp.Plugins.TeamMovements.Import;

public class ImportResults
{
    public int FilesProcessed { get; set; }
    public int ErrorCount { get; set; }
    public int MovementsCount { get; set; }
    public int ParticipantsCount { get; set; }
    public string? VerificationError { get; set; } = null;
}
