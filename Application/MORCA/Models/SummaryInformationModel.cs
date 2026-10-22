namespace MORCA.Models;

/// <summary>
/// The Summary Information stream decoded the way Orca presents it.
/// </summary>
public sealed record SummaryInformationModel(
    string Title,
    string Author,
    string Subject,
    string Comments,
    string Keywords,
    string Platform,            // Template, before the ';'
    string Languages,           // Template, after the ';'
    string PackageCode,         // Revision Number
    int Schema,                 // Page Count (minimum Windows Installer version)
    int Security,               // 0 = none, 2 = read-only recommended, 4 = read-only enforced
    bool ShortFileNames,        // Word Count bit 0
    bool Compressed,            // Word Count bit 1
    bool AdministrativeImage,   // Word Count bit 2
    bool UacCompliant);         // Word Count bit 3
