using ClientLedger.Data;
using ClientLedger.Data.Audit;
using ClientLedger.Foundation.Contracts;
using ClientLedger.Foundation.Platform;
using ClientLedger.Modules.Crm.Domain;
using ClientLedger.Security;

namespace ClientLedger.Modules.Crm.Data;

/// <summary>
/// 示例数据。空库首启是最差的体验，也是转化率杀手：先用三条真实感数据把界面填满，
/// 用户能立刻判断"这东西我要不要"。所有示例记录都带 demo_ 前缀，可一键清除。
/// </summary>
public sealed class CrmSeeder(
    IUnitOfWork unitOfWork,
    IDbConnectionFactory factory,
    ISettingsStore settings,
    IPermissionService permissions,
    IAuditWriter audit,
    IClock clock,
    IIdentityProvider identity,
    IAppLog log) : IDemoDataContributor, IModuleSeedContributor
{
    private const string SeedFlag = "crm.seed.completed";

    public string ModuleId => "crm";

    public Task EnsureSeedAsync(CancellationToken cancellationToken = default) =>
        EnsureAsync(cancellationToken);

    public async Task EnsureAsync(CancellationToken cancellationToken = default)
    {
        if (await settings.GetAsync(SeedFlag, cancellationToken).ConfigureAwait(false) == "1")
        {
            return;
        }

        await using var connection = await factory.OpenAsync(cancellationToken).ConfigureAwait(false);
        var existing = await connection.ScalarLongAsync(
            null,
            "SELECT COUNT(1) FROM crm_customer;",
            cancellationToken).ConfigureAwait(false);

        if (existing == 0)
        {
            try
            {
                await InsertDemoAsync(cancellationToken).ConfigureAwait(false);
                log.Info("已写入 CRM 示例数据。");
            }
            catch (Exception ex)
            {
                log.Error("写入示例数据失败，不影响正常使用。", ex);
            }
        }

        await settings.SetAsync(SeedFlag, "1", cancellationToken).ConfigureAwait(false);
    }

    /// <summary>只清理示例数据，真实数据一行不碰。</summary>
    public async Task<int> ClearDemoAsync(CancellationToken cancellationToken = default)
    {
        permissions.Demand(Permissions.PlatformDataReset);

        return await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var removed = await connection.ScalarLongAsync(
                transaction,
                "SELECT COUNT(1) FROM crm_customer WHERE id LIKE 'demo_%';",
                cancellationToken).ConfigureAwait(false);

            if (removed == 0)
            {
                return 0;
            }

            await connection.ExecuteAsync(
                transaction,
                "DELETE FROM crm_quote_line WHERE quote_id IN (SELECT id FROM crm_quote WHERE id LIKE 'demo_%');",
                cancellationToken).ConfigureAwait(false);

            await connection.ExecuteAsync(transaction, "DELETE FROM crm_quote WHERE id LIKE 'demo_%';", cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(transaction, "DELETE FROM crm_schedule WHERE id LIKE 'demo_%';", cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(transaction, "DELETE FROM crm_activity WHERE id LIKE 'demo_%';", cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(transaction, "DELETE FROM crm_contact WHERE id LIKE 'demo_%';", cancellationToken).ConfigureAwait(false);
            await connection.ExecuteAsync(transaction, "DELETE FROM crm_customer WHERE id LIKE 'demo_%';", cancellationToken).ConfigureAwait(false);

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.demo.clear",
                    EntityType = "customer",
                    EntityId = "demo",
                    Summary = $"清除示例客户 {removed} 个及其关联数据。"
                },
                cancellationToken).ConfigureAwait(false);

            return (int)removed;
        }, cancellationToken).ConfigureAwait(false);
    }

    private async Task InsertDemoAsync(CancellationToken cancellationToken)
    {
        var owner = identity.Actor;
        var now = clock.UtcNow;
        var month = now.ToLocalTime().ToString("yyyy-MM");

        var ruifeng = Ids.Demo("cus");
        var lanwan = Ids.Demo("cus");
        var hengda = Ids.Demo("cus");
        var yunqi = Ids.Demo("cus");
        var beichen = Ids.Demo("cus");

        await unitOfWork.ExecuteAsync(async (connection, transaction) =>
        {
            var customers = new[]
            {
                (Id: ruifeng, Name: "锐丰建材有限公司", Industry: "建筑材料", Scale: "50-100 人", Source: "老客户转介绍",
                    Grade: CustomerGrade.A, Status: CustomerStatus.Negotiating, Phone: "021-6688-1200",
                    Email: "wanghai@ruifeng-demo.cn", Address: "上海市嘉定区曹安公路 3888 号",
                    Tags: "年度框架,华东", Note: "关注交期与账期，偏好阶梯价。"),
                (Id: lanwan, Name: "蓝湾空间设计", Industry: "设计服务", Scale: "10-50 人", Source: "官网咨询",
                    Grade: CustomerGrade.B, Status: CustomerStatus.Qualified, Phone: "021-5521-0908",
                    Email: "lijing@lanwan-demo.cn", Address: "上海市徐汇区龙腾大道 2555 号",
                    Tags: "展厅项目,软装", Note: "预算区间 18-22 万，决策人重视方案质感。"),
                (Id: hengda, Name: "恒达精密机械", Industry: "机械制造", Scale: "100-500 人", Source: "行业展会",
                    Grade: CustomerGrade.A, Status: CustomerStatus.Won, Phone: "0512-6677-3311",
                    Email: "chenjianguo@hengda-demo.cn", Address: "苏州市吴中区木渎镇金枫路 18 号",
                    Tags: "老客户,工装夹具", Note: "验收流程规范，技术协议签署较快。"),
                (Id: yunqi, Name: "云栖酒店管理", Industry: "酒店服务", Scale: "50-100 人", Source: "朋友介绍",
                    Grade: CustomerGrade.C, Status: CustomerStatus.Lead, Phone: "0571-8899-2100",
                    Email: "zhoumin@yunqi-demo.cn", Address: "杭州市西湖区文三路 199 号",
                    Tags: "待触达", Note: "电话未接通，需换时段联系。"),
                (Id: beichen, Name: "北辰装饰工程", Industry: "装饰工程", Scale: "10-50 人", Source: "电话陌拜",
                    Grade: CustomerGrade.D, Status: CustomerStatus.Lost, Phone: "025-8321-7650",
                    Email: "", Address: "南京市鼓楼区中山北路 105 号",
                    Tags: "价格敏感", Note: "预算差距较大，半年后再触达。")
            };

            foreach (var c in customers)
            {
                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_customer
                        (id, name, industry, scale, source, grade, status, owner, phone, email, website, address, tags, note, created_utc, updated_utc, is_deleted)
                    VALUES
                        ($id, $name, $industry, $scale, $source, $grade, $status, $owner, $phone, $email, '', $address, $tags, $note, $created, $updated, 0);
                    """,
                    cancellationToken,
                    ("$id", c.Id),
                    ("$name", c.Name),
                    ("$industry", c.Industry),
                    ("$scale", c.Scale),
                    ("$source", c.Source),
                    ("$grade", c.Grade.ToString()),
                    ("$status", c.Status.ToString()),
                    ("$owner", owner),
                    ("$phone", c.Phone),
                    ("$email", c.Email),
                    ("$address", c.Address),
                    ("$tags", c.Tags),
                    ("$note", c.Note),
                    ("$created", SqliteValue.Db(now.AddDays(-40))),
                    ("$updated", SqliteValue.Db(now))).ConfigureAwait(false);
            }

            var contacts = new[]
            {
                (Customer: ruifeng, Name: "王海", Title: "采购经理", Phone: "138-0188-2201", Email: "wanghai@ruifeng-demo.cn", WeChat: "wanghai_sh", Primary: true),
                (Customer: lanwan, Name: "李静", Title: "设计总监", Phone: "139-1772-8803", Email: "lijing@lanwan-demo.cn", WeChat: "lijing_design", Primary: true),
                (Customer: hengda, Name: "陈建国", Title: "总经理", Phone: "136-0621-5578", Email: "chenjianguo@hengda-demo.cn", WeChat: "cjg_hengda", Primary: true),
                (Customer: hengda, Name: "赵磊", Title: "技术主管", Phone: "137-7180-2245", Email: "zhaolei@hengda-demo.cn", WeChat: "zhaolei_tech", Primary: false),
                (Customer: yunqi, Name: "周敏", Title: "运营总监", Phone: "135-8810-6632", Email: "zhoumin@yunqi-demo.cn", WeChat: "zhoumin_yq", Primary: true),
                (Customer: beichen, Name: "刘洋", Title: "项目经理", Phone: "133-2210-4498", Email: "", WeChat: "liuyang_bc", Primary: true)
            };

            foreach (var c in contacts)
            {
                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_contact
                        (id, customer_id, name, title, phone, email, wechat, is_primary, note, created_utc, updated_utc, is_deleted)
                    VALUES
                        ($id, $customer, $name, $title, $phone, $email, $wechat, $primary, '', $created, $updated, 0);
                    """,
                    cancellationToken,
                    ("$id", Ids.Demo("ct")),
                    ("$customer", c.Customer),
                    ("$name", c.Name),
                    ("$title", c.Title),
                    ("$phone", c.Phone),
                    ("$email", c.Email),
                    ("$wechat", c.WeChat),
                    ("$primary", c.Primary ? 1 : 0),
                    ("$created", SqliteValue.Db(now.AddDays(-30))),
                    ("$updated", SqliteValue.Db(now))).ConfigureAwait(false);
            }

            var primaryContact = new Dictionary<string, string>(StringComparer.Ordinal);
            var contactRows = await connection.QueryAsync(
                transaction,
                "SELECT id, customer_id FROM crm_contact WHERE is_primary = 1;",
                reader => (Id: reader.Str(0), Customer: reader.Str(1)),
                cancellationToken).ConfigureAwait(false);

            foreach (var row in contactRows)
            {
                primaryContact[row.Customer] = row.Id;
            }

            var activities = new[]
            {
                (Customer: ruifeng, Days: -2, Kind: ActivityKind.Call, Summary: "沟通年度供货框架，对方关注交期与账期，希望本月内给出阶梯价。",
                    Next: "整理阶梯报价并发给王海", Follow: 2),
                (Customer: ruifeng, Days: -9, Kind: ActivityKind.Visit, Summary: "到厂拜访，确认瓷砖规格与色号，样品间已拍照留档。",
                    Next: "寄送 3 组样品", Follow: 3),
                (Customer: lanwan, Days: -1, Kind: ActivityKind.WeChat, Summary: "对方发来展厅平面图，希望先看软装清单。",
                    Next: "出软装清单初稿", Follow: 1),
                (Customer: lanwan, Days: -6, Kind: ActivityKind.Meeting, Summary: "与设计总监确认动线与灯光方案，预算区间 18-22 万。",
                    Next: "准备报价", Follow: 4),
                (Customer: hengda, Days: -1, Kind: ActivityKind.Call, Summary: "确认工装夹具验收标准，技术协议无异议。",
                    Next: "走合同盖章流程", Follow: 1),
                (Customer: hengda, Days: -12, Kind: ActivityKind.Visit, Summary: "现场测量三台设备底座尺寸。",
                    Next: "", Follow: 0),
                (Customer: yunqi, Days: -3, Kind: ActivityKind.Call, Summary: "前台转接后未接通，留下名片。",
                    Next: "换个时间段再联系", Follow: 2),
                (Customer: beichen, Days: -20, Kind: ActivityKind.Email, Summary: "方案报价超出预算，对方选择本地供应商。",
                    Next: "半年后再触达", Follow: 0)
            };

            foreach (var a in activities)
            {
                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_activity
                        (id, customer_id, contact_id, kind, occurred_utc, summary, next_step, next_follow_utc, owner, created_utc, updated_utc, is_deleted)
                    VALUES
                        ($id, $customer, $contact, $kind, $occurred, $summary, $next, $follow, $owner, $created, $updated, 0);
                    """,
                    cancellationToken,
                    ("$id", Ids.Demo("act")),
                    ("$customer", a.Customer),
                    ("$contact", primaryContact.GetValueOrDefault(a.Customer)),
                    ("$kind", a.Kind.ToString()),
                    ("$occurred", SqliteValue.Db(now.AddDays(a.Days))),
                    ("$summary", a.Summary),
                    ("$next", a.Next),
                    ("$follow", a.Follow == 0 ? null : SqliteValue.Db(now.AddDays(a.Follow))),
                    ("$owner", owner),
                    ("$created", SqliteValue.Db(now.AddDays(a.Days))),
                    ("$updated", SqliteValue.Db(now.AddDays(a.Days)))).ConfigureAwait(false);
            }

            var schedules = new[]
            {
                (Title: "拜访锐丰建材 · 确认样品", Kind: ScheduleKind.Meeting, Customer: ruifeng, Day: 0, Hour: 10, Minute: 0, Duration: 60, Remind: 30),
                (Title: "回访蓝湾设计 · 软装清单", Kind: ScheduleKind.FollowUp, Customer: lanwan, Day: 0, Hour: 15, Minute: 30, Duration: 30, Remind: 15),
                (Title: "寄送锐丰样品（顺丰）", Kind: ScheduleKind.Task, Customer: ruifeng, Day: 1, Hour: 9, Minute: 30, Duration: 0, Remind: 15),
                (Title: "恒达机械合同盖章", Kind: ScheduleKind.Meeting, Customer: hengda, Day: 2, Hour: 14, Minute: 0, Duration: 90, Remind: 60),
                (Title: "整理本周跟进记录", Kind: ScheduleKind.Task, Customer: null, Day: 0, Hour: 17, Minute: 30, Duration: 30, Remind: 15)
            };

            foreach (var s in schedules)
            {
                var startLocal = clock.UtcNow.ToLocalTime().Date.AddDays(s.Day).AddHours(s.Hour).AddMinutes(s.Minute);
                var start = new DateTimeOffset(startLocal, TimeZoneInfo.Local.GetUtcOffset(startLocal));

                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_schedule
                        (id, title, kind, customer_id, start_utc, end_utc, remind_minutes, state, note, owner, created_utc, updated_utc)
                    VALUES
                        ($id, $title, $kind, $customer, $start, $end, $remind, 'Pending', '', $owner, $created, $updated);
                    """,
                    cancellationToken,
                    ("$id", Ids.Demo("sch")),
                    ("$title", s.Title),
                    ("$kind", s.Kind.ToString()),
                    ("$customer", s.Customer),
                    ("$start", SqliteValue.Db(start)),
                    ("$end", s.Duration == 0 ? null : SqliteValue.Db(start.AddMinutes(s.Duration))),
                    ("$remind", s.Remind),
                    ("$owner", owner),
                    ("$created", SqliteValue.Db(now)),
                    ("$updated", SqliteValue.Db(now))).ConfigureAwait(false);
            }

            var quotes = new[]
            {
                (Id: Ids.Demo("qt"), Number: $"Q{month}-001", Customer: ruifeng, Title: "Q4 瓷砖供货报价",
                    Status: QuoteStatus.Sent, Discount: 3m, Tax: 13m, ValidDays: 15,
                    Lines: new[]
                    {
                        (Name: "通体大理石瓷砖 800×800", Spec: "浅灰 / 哑光", Unit: "片", Qty: 1200m, Price: 86m, Discount: 0m),
                        (Name: "木纹砖 200×1200", Spec: "原木色 / 防滑", Unit: "片", Qty: 800m, Price: 62m, Discount: 5m)
                    }),
                (Id: Ids.Demo("qt"), Number: $"Q{month}-002", Customer: hengda, Title: "工装夹具定制（三套）",
                    Status: QuoteStatus.Accepted, Discount: 0m, Tax: 13m, ValidDays: 30,
                    Lines: new[]
                    {
                        (Name: "液压夹具 HT-300", Spec: "含定位销", Unit: "套", Qty: 3m, Price: 18500m, Discount: 0m),
                        (Name: "现场安装调试", Spec: "含培训", Unit: "项", Qty: 1m, Price: 4200m, Discount: 0m)
                    }),
                (Id: Ids.Demo("qt"), Number: $"Q{month}-003", Customer: lanwan, Title: "展厅软装方案",
                    Status: QuoteStatus.Draft, Discount: 5m, Tax: 6m, ValidDays: 20,
                    Lines: new[]
                    {
                        (Name: "软装陈设包", Spec: "含灯具、布艺、饰品", Unit: "套", Qty: 1m, Price: 128000m, Discount: 0m),
                        (Name: "现场调整服务", Spec: "3 次到场", Unit: "项", Qty: 1m, Price: 6000m, Discount: 0m)
                    })
            };

            foreach (var q in quotes)
            {
                await connection.ExecuteAsync(
                    transaction,
                    """
                    INSERT INTO crm_quote
                        (id, number, customer_id, title, status, currency, discount_percent, tax_percent, valid_until, note, created_utc, updated_utc, is_deleted)
                    VALUES
                        ($id, $number, $customer, $title, $status, 'CNY', $discount, $tax, $valid, '', $created, $updated, 0);
                    """,
                    cancellationToken,
                    ("$id", q.Id),
                    ("$number", q.Number),
                    ("$customer", q.Customer),
                    ("$title", q.Title),
                    ("$status", q.Status.ToString()),
                    ("$discount", SqliteValue.Db(q.Discount)),
                    ("$tax", SqliteValue.Db(q.Tax)),
                    ("$valid", SqliteValue.Db(now.AddDays(q.ValidDays))),
                    ("$created", SqliteValue.Db(now.AddDays(-5))),
                    ("$updated", SqliteValue.Db(now.AddDays(-1)))).ConfigureAwait(false);

                var position = 0;
                foreach (var line in q.Lines)
                {
                    await connection.ExecuteAsync(
                        transaction,
                        """
                        INSERT INTO crm_quote_line
                            (id, quote_id, position, name, spec, unit, quantity, unit_price, discount_percent)
                        VALUES
                            ($id, $quote, $position, $name, $spec, $unit, $qty, $price, $discount);
                        """,
                        cancellationToken,
                        ("$id", Ids.Demo("ql")),
                        ("$quote", q.Id),
                        ("$position", position++),
                        ("$name", line.Name),
                        ("$spec", line.Spec),
                        ("$unit", line.Unit),
                        ("$qty", SqliteValue.Db(line.Qty)),
                        ("$price", SqliteValue.Db(line.Price)),
                        ("$discount", SqliteValue.Db(line.Discount))).ConfigureAwait(false);
                }
            }

            await audit.WriteAsync(
                transaction,
                new AuditEntry
                {
                    ModuleId = "crm",
                    Action = "crm.demo.seed",
                    EntityType = "customer",
                    EntityId = "demo",
                    Summary = "写入示例数据（5 个客户、6 位联系人、8 条跟进、5 个日程、3 份报价）。"
                },
                cancellationToken).ConfigureAwait(false);
        }, cancellationToken).ConfigureAwait(false);
    }
}