using ClientLedger.Data;

namespace ClientLedger.Modules.Crm.Data;

/// <summary>
/// CRM 模块的全部表结构。模块自带迁移、自带表前缀（crm_），
/// 宿主与其它模块永远不需要知道这些表的存在。
/// </summary>
public sealed class CrmMigrationSource : IMigrationSource
{
    public const string ModuleId = "crm";

    public IReadOnlyList<MigrationScript> GetMigrations() =>
    [
        new(ModuleId, 1, "客户、联系人、跟进、日程、报价", """
            CREATE TABLE IF NOT EXISTS crm_customer (
                id          TEXT PRIMARY KEY,
                name        TEXT NOT NULL,
                industry    TEXT NOT NULL DEFAULT '',
                scale       TEXT NOT NULL DEFAULT '',
                source      TEXT NOT NULL DEFAULT '',
                grade       TEXT NOT NULL DEFAULT 'C',
                status      TEXT NOT NULL DEFAULT 'Lead',
                owner       TEXT NOT NULL,
                phone       TEXT NOT NULL DEFAULT '',
                email       TEXT NOT NULL DEFAULT '',
                website     TEXT NOT NULL DEFAULT '',
                address     TEXT NOT NULL DEFAULT '',
                tags        TEXT NOT NULL DEFAULT '',
                note        TEXT NOT NULL DEFAULT '',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                is_deleted  INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_crm_customer_status ON crm_customer(status, is_deleted);
            CREATE INDEX IF NOT EXISTS ix_crm_customer_owner ON crm_customer(owner, is_deleted);
            CREATE INDEX IF NOT EXISTS ix_crm_customer_name ON crm_customer(name);

            CREATE TABLE IF NOT EXISTS crm_contact (
                id          TEXT PRIMARY KEY,
                customer_id TEXT NOT NULL REFERENCES crm_customer(id),
                name        TEXT NOT NULL,
                title       TEXT NOT NULL DEFAULT '',
                phone       TEXT NOT NULL DEFAULT '',
                email       TEXT NOT NULL DEFAULT '',
                wechat      TEXT NOT NULL DEFAULT '',
                is_primary  INTEGER NOT NULL DEFAULT 0,
                note        TEXT NOT NULL DEFAULT '',
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL,
                is_deleted  INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_crm_contact_customer ON crm_contact(customer_id, is_deleted);

            CREATE TABLE IF NOT EXISTS crm_activity (
                id              TEXT PRIMARY KEY,
                customer_id     TEXT NOT NULL REFERENCES crm_customer(id),
                contact_id      TEXT NULL REFERENCES crm_contact(id),
                kind            TEXT NOT NULL DEFAULT 'Call',
                occurred_utc    TEXT NOT NULL,
                summary         TEXT NOT NULL,
                next_step       TEXT NOT NULL DEFAULT '',
                next_follow_utc TEXT NULL,
                owner           TEXT NOT NULL,
                created_utc     TEXT NOT NULL,
                updated_utc     TEXT NOT NULL,
                is_deleted      INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_crm_activity_customer ON crm_activity(customer_id, occurred_utc DESC);
            CREATE INDEX IF NOT EXISTS ix_crm_activity_occurred ON crm_activity(occurred_utc DESC);

            CREATE TABLE IF NOT EXISTS crm_schedule (
                id             TEXT PRIMARY KEY,
                title          TEXT NOT NULL,
                kind           TEXT NOT NULL DEFAULT 'Task',
                customer_id    TEXT NULL REFERENCES crm_customer(id),
                start_utc      TEXT NOT NULL,
                end_utc        TEXT NULL,
                remind_minutes INTEGER NOT NULL DEFAULT 15,
                state          TEXT NOT NULL DEFAULT 'Pending',
                note           TEXT NOT NULL DEFAULT '',
                owner          TEXT NOT NULL,
                created_utc    TEXT NOT NULL,
                updated_utc    TEXT NOT NULL
            );

            CREATE INDEX IF NOT EXISTS ix_crm_schedule_start ON crm_schedule(start_utc);
            CREATE INDEX IF NOT EXISTS ix_crm_schedule_state ON crm_schedule(state, start_utc);

            CREATE TABLE IF NOT EXISTS crm_quote (
                id               TEXT PRIMARY KEY,
                number           TEXT NOT NULL UNIQUE,
                customer_id      TEXT NOT NULL REFERENCES crm_customer(id),
                title            TEXT NOT NULL,
                status           TEXT NOT NULL DEFAULT 'Draft',
                currency         TEXT NOT NULL DEFAULT 'CNY',
                discount_percent TEXT NOT NULL DEFAULT '0',
                tax_percent      TEXT NOT NULL DEFAULT '0',
                valid_until      TEXT NULL,
                note             TEXT NOT NULL DEFAULT '',
                created_utc      TEXT NOT NULL,
                updated_utc      TEXT NOT NULL,
                is_deleted       INTEGER NOT NULL DEFAULT 0
            );

            CREATE INDEX IF NOT EXISTS ix_crm_quote_customer ON crm_quote(customer_id, is_deleted);
            CREATE INDEX IF NOT EXISTS ix_crm_quote_status ON crm_quote(status, is_deleted);

            CREATE TABLE IF NOT EXISTS crm_quote_line (
                id               TEXT PRIMARY KEY,
                quote_id         TEXT NOT NULL REFERENCES crm_quote(id) ON DELETE CASCADE,
                position         INTEGER NOT NULL,
                name             TEXT NOT NULL,
                spec             TEXT NOT NULL DEFAULT '',
                unit             TEXT NOT NULL DEFAULT '',
                quantity         TEXT NOT NULL DEFAULT '0',
                unit_price       TEXT NOT NULL DEFAULT '0',
                discount_percent TEXT NOT NULL DEFAULT '0'
            );

            CREATE INDEX IF NOT EXISTS ix_crm_quote_line_quote ON crm_quote_line(quote_id, position);
            """)
    ];
}