namespace ClientLedger.Data;

/// <summary>
/// 一段带版本的迁移脚本。模块自带脚本、自带 module_id，
/// 宿主只负责按模块分别记账，绝不跨模块合并版本号。
/// </summary>
public sealed record MigrationScript(string ModuleId, int Version, string Description, string Sql);

public interface IMigrationSource
{
    IReadOnlyList<MigrationScript> GetMigrations();
}

/// <summary>
/// 平台自身的表：版本台账、键值设置、只追加审计。
/// 这三张表不允许任何业务模块写入（审计只能通过 IAuditWriter）。
/// </summary>
public sealed class PlatformMigrationSource : IMigrationSource
{
    public const string ModuleId = "platform";

    public IReadOnlyList<MigrationScript> GetMigrations() =>
    [
        new(ModuleId, 1, "平台基线：版本台账、设置、审计", """
            CREATE TABLE IF NOT EXISTS platform_schema_version (
                module_id   TEXT    NOT NULL,
                version     INTEGER NOT NULL,
                description TEXT    NOT NULL DEFAULT '',
                applied_utc TEXT    NOT NULL,
                PRIMARY KEY (module_id, version)
            );

            CREATE TABLE IF NOT EXISTS platform_settings (
                key         TEXT PRIMARY KEY,
                value       TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS platform_audit (
                id          INTEGER PRIMARY KEY AUTOINCREMENT,
                at_utc      TEXT    NOT NULL,
                module_id   TEXT    NOT NULL,
                actor       TEXT    NOT NULL,
                machine     TEXT    NOT NULL,
                app_version TEXT    NOT NULL,
                action      TEXT    NOT NULL,
                entity_type TEXT    NOT NULL,
                entity_id   TEXT    NOT NULL,
                summary     TEXT    NULL,
                before_json TEXT    NULL,
                after_json  TEXT    NULL
            );

            CREATE INDEX IF NOT EXISTS ix_platform_audit_at
                ON platform_audit(at_utc DESC);

            CREATE INDEX IF NOT EXISTS ix_platform_audit_entity
                ON platform_audit(entity_type, entity_id);
            """)
    ];
}