using MORCA.Models;

namespace MORCA.Services;

/// <summary>
/// The standard Windows Installer tables (the ones Orca offers under Tables &gt; Add Table): their names and
/// the columns each has, with type, size, key and nullable flags.
///
/// Each column is written "Name code", with a leading '*' on primary key columns, and the code is the one
/// used in .idt files: i2 or i4 (integer), s&lt;n&gt; (string), l&lt;n&gt; (localizable string), v0 (binary);
/// uppercase means the column is nullable. Primary key columns come first. The structure follows the Windows
/// Installer database schema as defined by the WiX Toolset (WindowsInstallerTableDefinitions), without the
/// WiX-internal tables and columns and the patch creation (PCP) tables. _Validation is the one underscore
/// table that is offered, as it is in Orca.
/// </summary>
public static class StandardTables
{
    private static readonly (string Name, string[] Columns)[] Specs =
    {
        ("_Validation", new[] { "*Table s32", "*Column s32", "Nullable s4", "MinValue I4", "MaxValue I4", "KeyTable S255", "KeyColumn I2", "Category S32", "Set S255", "Description S255" }),
        ("ActionText", new[] { "*Action s72", "Description L0", "Template L0" }),
        ("AdminExecuteSequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("AdminUISequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("AdvtExecuteSequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("AdvtUISequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("AppId", new[] { "*AppId s38", "RemoteServerName S255", "LocalService S255", "ServiceParameters S255", "DllSurrogate S255", "ActivateAtStorage I2", "RunAsInteractiveUser I2" }),
        ("AppSearch", new[] { "*Property s72", "*Signature_ s72" }),
        ("BBControl", new[] { "*Billboard_ s50", "*BBControl s50", "Type s50", "X i2", "Y i2", "Width i2", "Height i2", "Attributes I4", "Text L50" }),
        ("Billboard", new[] { "*Billboard s50", "Feature_ s38", "Action S50", "Ordering I2" }),
        ("Binary", new[] { "*Name s72", "Data v0" }),
        ("BindImage", new[] { "*File_ s72", "Path S255" }),
        ("CCPSearch", new[] { "*Signature_ s72" }),
        ("CheckBox", new[] { "*Property s72", "Value S64" }),
        ("Class", new[] { "*CLSID s38", "*Context s32", "*Component_ s72", "ProgId_Default S255", "Description L255", "AppId_ S38", "FileTypeMask S255", "Icon_ S72", "IconIndex I2", "DefInprocHandler S32", "Argument S255", "Feature_ s38", "Attributes I2" }),
        ("ComboBox", new[] { "*Property s72", "*Order i2", "Value s64", "Text L64" }),
        ("CompLocator", new[] { "*Signature_ s72", "ComponentId s38", "Type I2" }),
        ("Complus", new[] { "*Component_ s72", "ExpType I2" }),
        ("Component", new[] { "*Component s72", "ComponentId S38", "Directory_ s72", "Attributes i2", "Condition S255", "KeyPath S72" }),
        ("Condition", new[] { "*Feature_ s38", "*Level i2", "Condition S255" }),
        ("Control", new[] { "*Dialog_ s72", "*Control s50", "Type s20", "X i2", "Y i2", "Width i2", "Height i2", "Attributes I4", "Property S72", "Text L0", "Control_Next S50", "Help L50" }),
        ("ControlCondition", new[] { "*Dialog_ s72", "*Control_ s50", "*Action s50", "*Condition s255" }),
        ("ControlEvent", new[] { "*Dialog_ s72", "*Control_ s50", "*Event s50", "*Argument s255", "*Condition S255", "Ordering I2" }),
        ("CreateFolder", new[] { "*Directory_ s72", "*Component_ s72" }),
        ("CustomAction", new[] { "*Action s72", "Type i2", "Source S72", "Target S255", "ExtendedType I4" }),
        ("Dialog", new[] { "*Dialog s72", "HCentering i2", "VCentering i2", "Width i2", "Height i2", "Attributes I4", "Title L128", "Control_First s50", "Control_Default S50", "Control_Cancel S50" }),
        ("Directory", new[] { "*Directory s72", "Directory_Parent S72", "DefaultDir l255" }),
        ("DrLocator", new[] { "*Signature_ s72", "*Parent S72", "*Path S255", "Depth I2" }),
        ("DuplicateFile", new[] { "*FileKey s72", "Component_ s72", "File_ s72", "DestName L255", "DestFolder S72" }),
        ("Environment", new[] { "*Environment s72", "Name l255", "Value L255", "Component_ s72" }),
        ("Error", new[] { "*Error i2", "Message L0" }),
        ("EventMapping", new[] { "*Dialog_ s72", "*Control_ s50", "*Event s50", "Attribute s50" }),
        ("Extension", new[] { "*Extension s255", "*Component_ s72", "ProgId_ S255", "MIME_ S64", "Feature_ s38" }),
        ("Feature", new[] { "*Feature s38", "Feature_Parent S38", "Title L64", "Description L255", "Display I2", "Level i2", "Directory_ S72", "Attributes i2" }),
        ("FeatureComponents", new[] { "*Feature_ s38", "*Component_ s72" }),
        ("File", new[] { "*File s72", "Component_ s72", "FileName l255", "FileSize i4", "Version S72", "Language S20", "Attributes I2", "Sequence i4" }),
        ("FileSFPCatalog", new[] { "*File_ s72", "*SFPCatalog_ s255" }),
        ("Font", new[] { "*File_ s72", "FontTitle S128" }),
        ("Icon", new[] { "*Name s72", "Data v0" }),
        ("IniFile", new[] { "*IniFile s72", "FileName l255", "DirProperty S72", "Section l96", "Key l128", "Value l255", "Action i2", "Component_ s72" }),
        ("IniLocator", new[] { "*Signature_ s72", "FileName s255", "Section s96", "Key s128", "Field I2", "Type I2" }),
        ("InstallExecuteSequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("InstallUISequence", new[] { "*Action s72", "Condition S255", "Sequence I2" }),
        ("IsolatedComponent", new[] { "*Component_Shared s72", "*Component_Application s72" }),
        ("LaunchCondition", new[] { "*Condition s255", "Description l255" }),
        ("ListBox", new[] { "*Property s72", "*Order i2", "Value s64", "Text L64" }),
        ("ListView", new[] { "*Property s72", "*Order i2", "Value s64", "Text L64", "Binary_ S72" }),
        ("LockPermissions", new[] { "*LockObject s72", "*Table s32", "*Domain S255", "*User s255", "Permission I4" }),
        ("Media", new[] { "*DiskId i2", "LastSequence i4", "DiskPrompt L64", "Cabinet S255", "VolumeLabel S32", "Source S72" }),
        ("MIME", new[] { "*ContentType s64", "Extension_ s255", "CLSID S38" }),
        ("ModuleAdminExecuteSequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleAdminUISequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleAdvtExecuteSequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleAdvtUISequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleComponents", new[] { "*Component s72", "*ModuleID s72", "*Language i2" }),
        ("ModuleConfiguration", new[] { "*Name s72", "Format i2", "Type S72", "ContextData L0", "DefaultValue L0", "Attributes I4", "DisplayName L72", "Description L0", "HelpLocation S0", "HelpKeyword S0" }),
        ("ModuleDependency", new[] { "*ModuleID s72", "*ModuleLanguage i2", "*RequiredID s72", "*RequiredLanguage i2", "RequiredVersion S32" }),
        ("ModuleExclusion", new[] { "*ModuleID s72", "*ModuleLanguage i2", "*ExcludedID s72", "*ExcludedLanguage i2", "ExcludedMinVersion S32", "ExcludedMaxVersion S32" }),
        ("ModuleIgnoreTable", new[] { "*Table s72" }),
        ("ModuleInstallExecuteSequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleInstallUISequence", new[] { "*Action s64", "Sequence I2", "BaseAction S64", "After I2", "Condition S255" }),
        ("ModuleSignature", new[] { "*ModuleID s72", "*Language i2", "Version s32" }),
        ("ModuleSubstitution", new[] { "*Table s72", "*Row s0", "*Column s72", "Value L0" }),
        ("MoveFile", new[] { "*FileKey s72", "Component_ s72", "SourceName L255", "DestName L255", "SourceFolder S72", "DestFolder s72", "Options i2" }),
        ("MsiAssembly", new[] { "*Component_ s72", "Feature_ s38", "File_Manifest S72", "File_Application S72", "Attributes I2" }),
        ("MsiAssemblyName", new[] { "*Component_ s72", "*Name s255", "Value s255" }),
        ("MsiDigitalCertificate", new[] { "*DigitalCertificate s72", "CertData v0" }),
        ("MsiDigitalSignature", new[] { "*Table s32", "*SignObject s72", "DigitalCertificate_ s72", "Hash V0" }),
        ("MsiEmbeddedChainer", new[] { "*MsiEmbeddedChainer s72", "Condition S255", "CommandLine S255", "Source s72", "Type i2" }),
        ("MsiEmbeddedUI", new[] { "*MsiEmbeddedUI s72", "FileName l255", "Attributes i2", "MessageFilter I4", "Data v0" }),
        ("MsiFileHash", new[] { "*File_ s72", "Options i2", "HashPart1 i4", "HashPart2 i4", "HashPart3 i4", "HashPart4 i4" }),
        ("MsiLockPermissionsEx", new[] { "*MsiLockPermissionsEx s72", "LockObject s72", "Table s32", "SDDLText s0", "Condition S255" }),
        ("MsiPackageCertificate", new[] { "*PackageCertificate s72", "*DigitalCertificate_ s72" }),
        ("MsiPatchCertificate", new[] { "*PatchCertificate s72", "*DigitalCertificate_ s72" }),
        ("MsiPatchHeaders", new[] { "*StreamRef s38", "Header v0" }),
        ("MsiPatchMetadata", new[] { "*Company S72", "*Property s72", "Value l0" }),
        ("MsiPatchOldAssemblyFile", new[] { "*File_ s72", "*Assembly_ s72" }),
        ("MsiPatchOldAssemblyName", new[] { "*Assembly s72", "*Name s255", "Value s255" }),
        ("MsiPatchSequence", new[] { "*PatchFamily s72", "*ProductCode S38", "Sequence s72", "Attributes I4" }),
        ("MsiServiceConfig", new[] { "*MsiServiceConfig s72", "Name l255", "Event i2", "ConfigType i4", "Argument S0", "Component_ s72" }),
        ("MsiServiceConfigFailureActions", new[] { "*MsiServiceConfigFailureActions s72", "Name l255", "Event i2", "ResetPeriod I4", "RebootMessage L255", "Command L255", "Actions S0", "DelayActions S0", "Component_ s72" }),
        ("MsiShortcutProperty", new[] { "*MsiShortcutProperty s72", "Shortcut_ s72", "PropertyKey s0", "PropVariantValue s0" }),
        ("ODBCAttribute", new[] { "*Driver_ s72", "*Attribute s40", "Value L255" }),
        ("ODBCDataSource", new[] { "*DataSource s72", "Component_ s72", "Description s255", "DriverDescription s255", "Registration i2" }),
        ("ODBCDriver", new[] { "*Driver s72", "Component_ s72", "Description s255", "File_ s72", "File_Setup S72" }),
        ("ODBCSourceAttribute", new[] { "*DataSource_ s72", "*Attribute s32", "Value L255" }),
        ("ODBCTranslator", new[] { "*Translator s72", "Component_ s72", "Description s255", "File_ s72", "File_Setup S72" }),
        ("Patch", new[] { "*File_ s72", "*Sequence i4", "PatchSize i4", "Attributes i2", "Header V0", "StreamRef_ S38" }),
        ("PatchPackage", new[] { "*PatchId s38", "Media_ i2" }),
        ("ProgId", new[] { "*ProgId s255", "ProgId_Parent S255", "Class_ S38", "Description L255", "Icon_ S72", "IconIndex I2" }),
        ("Property", new[] { "*Property s72", "Value l0" }),
        ("PublishComponent", new[] { "*ComponentId s38", "*Qualifier s255", "*Component_ s72", "AppData L0", "Feature_ s38" }),
        ("RadioButton", new[] { "*Property s72", "*Order i2", "Value s64", "X i2", "Y i2", "Width i2", "Height i2", "Text L0", "Help L50" }),
        ("Registry", new[] { "*Registry s72", "Root i2", "Key l255", "Name L255", "Value L0", "Component_ s72" }),
        ("RegLocator", new[] { "*Signature_ s72", "Root i2", "Key s255", "Name S255", "Type I2" }),
        ("RemoveFile", new[] { "*FileKey s72", "Component_ s72", "FileName L255", "DirProperty s72", "InstallMode i2" }),
        ("RemoveIniFile", new[] { "*RemoveIniFile s72", "FileName l255", "DirProperty S72", "Section l96", "Key l128", "Value L255", "Action i2", "Component_ s72" }),
        ("RemoveRegistry", new[] { "*RemoveRegistry s72", "Root i2", "Key l255", "Name L255", "Component_ s72" }),
        ("ReserveCost", new[] { "*ReserveKey s72", "Component_ s72", "ReserveFolder S72", "ReserveLocal i4", "ReserveSource i4" }),
        ("SelfReg", new[] { "*File_ s72", "Cost I2" }),
        ("ServiceControl", new[] { "*ServiceControl s72", "Name l255", "Event i2", "Arguments L255", "Wait I2", "Component_ s72" }),
        ("ServiceInstall", new[] { "*ServiceInstall s72", "Name s255", "DisplayName L255", "ServiceType i4", "StartType i4", "ErrorControl i4", "LoadOrderGroup S255", "Dependencies S255", "StartName S255", "Password S255", "Arguments S255", "Component_ s72", "Description L255" }),
        ("SFPCatalog", new[] { "*SFPCatalog s255", "Catalog v0", "Dependency S0" }),
        ("Shortcut", new[] { "*Shortcut s72", "Directory_ s72", "Name l128", "Component_ s72", "Target s72", "Arguments S255", "Description L255", "Hotkey I2", "Icon_ S72", "IconIndex I2", "ShowCmd I2", "WkDir S72", "DisplayResourceDLL S255", "DisplayResourceId I2", "DescriptionResourceDLL S255", "DescriptionResourceId I2" }),
        ("Signature", new[] { "*Signature s72", "FileName s255", "MinVersion S20", "MaxVersion S20", "MinSize I4", "MaxSize I4", "MinDate I4", "MaxDate I4", "Languages S255" }),
        ("SoftwareIdentificationTag", new[] { "*File_ s72", "Regid s0", "TagId s0", "PersistentId s0", "Alias S0" }),
        ("TextStyle", new[] { "*TextStyle s72", "FaceName s32", "Size i2", "Color I4", "StyleBits I2" }),
        ("TypeLib", new[] { "*LibID s38", "*Language i2", "*Component_ s72", "Version I4", "Description L128", "Directory_ S72", "Feature_ s38", "Cost I4" }),
        ("UIText", new[] { "*Key s72", "Text L255" }),
        ("Upgrade", new[] { "*UpgradeCode s38", "*VersionMin S20", "*VersionMax S20", "*Language S255", "*Attributes i4", "Remove S255", "ActionProperty s72" }),
        ("Verb", new[] { "*Extension_ s255", "*Verb s32", "Sequence I2", "Command L255", "Argument L255" }),
    };

    private static readonly Lazy<Dictionary<string, MsiTableDefinition>> Tables = new(() =>
        Specs.ToDictionary(s => s.Name, s => Build(s.Name, s.Columns), StringComparer.OrdinalIgnoreCase));

    /// <summary>The names of the standard tables, in alphabetical order, with _Validation first.</summary>
    public static IReadOnlyList<string> Names { get; } = Specs.Select(s => s.Name).ToList();

    /// <summary>A new definition of the named standard table (any capitalisation), or null if there is none.</summary>
    public static MsiTableDefinition? Get(string name) =>
        Tables.Value.TryGetValue(name, out var table) ? table.Clone() : null;

    private static MsiTableDefinition Build(string name, string[] columns)
    {
        var table = new MsiTableDefinition { Name = name };
        foreach (var spec in columns)
        {
            var isKey = spec[0] == '*';
            var parts = (isKey ? spec[1..] : spec).Split(' ');
            var code = parts[1];
            var size = int.Parse(code[1..]);

            var kind = char.ToLowerInvariant(code[0]) switch
            {
                'i' => size == 4 ? MsiColumnKind.LongInteger : MsiColumnKind.Integer,
                'l' => MsiColumnKind.LocalizableString,
                'v' => MsiColumnKind.Binary,
                _ => MsiColumnKind.String
            };

            table.Columns.Add(new MsiColumnDefinition
            {
                Name = parts[0],
                Kind = kind,
                Length = kind.HasLength() ? size : 0,
                Nullable = char.IsUpper(code[0])
            });
            if (isKey) table.PrimaryKeys++;
        }
        return table;
    }
}
