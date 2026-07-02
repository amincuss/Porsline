using System.Text.Json;
using PorslineClone.Application.Contracts;
using PorslineClone.Domain.Entities;

namespace PorslineClone.Infrastructure.Services.SmsPatterns;

public sealed record SmsPatternSeed(
    string Key,
    string Title,
    string Category,
    string Icon,
    string IconColor,
    string Template,
    SmsPatternPlaceholderDto[] Placeholders,
    string? Description,
    int SortOrder);

public static class SmsPatternDefaults
{
    private static readonly JsonSerializerOptions JsonOpts = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static IReadOnlyList<SmsPatternSeed> All { get; } = BuildAll();

    public static SmsPatternSeed? Find(string key) =>
        All.FirstOrDefault(x => string.Equals(x.Key, key, StringComparison.OrdinalIgnoreCase));

    public static string? GetTemplate(string key) => Find(key)?.Template;

    public static SmsPattern ToEntity(SmsPatternSeed seed) => new()
    {
        Id = Guid.NewGuid(),
        Key = seed.Key,
        Title = seed.Title,
        Category = seed.Category,
        Icon = seed.Icon,
        IconColor = seed.IconColor,
        Template = seed.Template,
        PlaceholdersJson = JsonSerializer.Serialize(seed.Placeholders, JsonOpts),
        Description = seed.Description,
        SortOrder = seed.SortOrder,
        IsActive = true,
        UpdatedAtUtc = DateTime.UtcNow,
    };

    private static SmsPatternPlaceholderDto P(string key, string label, string? sample = null) =>
        new(key, label, sample);

    private static IReadOnlyList<SmsPatternSeed> BuildAll()
    {
        var list = new List<SmsPatternSeed>();
        var order = 0;

        void Add(
            string key,
            string title,
            string category,
            string icon,
            string iconColor,
            string template,
            SmsPatternPlaceholderDto[] placeholders,
            string? description = null)
        {
            list.Add(new SmsPatternSeed(key, title, category, icon, iconColor, template, placeholders, description, order++));
        }

        // ── auth ──
        Add("auth.otp.login", "کد OTP ورود", "auth", "Shield", "#6366F1",
            "کد ورود شما: {code}",
            [P("code", "کد یک‌بار مصرف", "123456")],
            "ارسال هنگام ورود با OTP");

        Add("form.otp.access", "کد OTP فرم عمومی", "auth", "KeyRound", "#6366F1",
            "کد تایید فرم: {code}",
            [P("code", "کد تأیید", "654321")],
            "OTP برای باز کردن لینک فرم");

        // ── user ──
        Add("user.welcome.create", "خوش‌آمد ساخت کاربر", "user", "UserPlus", "#0EA5E9",
            "کارشناس محترم {firstName} {lastName}،\nکاربری شما در تاریخ {dateStr} ساعت {timeStr} ساخته شد.\nجهت استفاده از پنل به لینک زیر مراجعه نمایید:\n{loginUrl}",
            [
                P("firstName", "نام", "علی"),
                P("lastName", "نام خانوادگی", "رضایی"),
                P("dateStr", "تاریخ", "1404/03/08"),
                P("timeStr", "ساعت", "14:30"),
                P("loginUrl", "لینک ورود", "https://example.com/login"),
            ],
            "پیامک خوش‌آمد پس از ایجاد کاربر");

        // ── form: dispatch ──
        Add("form.dispatch.link.default", "ارسال لینک فرم (خودکار)", "form", "Send", "#10B981",
            "سلام {firstName} {lastName}\nفرم «{formTitle}» برای شما ارسال شد.\nلطفا از لینک زیر تکمیل کنید:\n{link}",
            [
                P("firstName", "نام", "علی"),
                P("lastName", "نام خانوادگی", "رضایی"),
                P("fullName", "نام کامل (نام + خانوادگی)", "علی رضایی"),
                P("formTitle", "عنوان فرم", "فرم ارزیابی"),
                P("link", "لینک فرم", "https://example.com/f/abc"),
            ],
            "ارسال تکی/گروهی لینک فرم — حالت خودکار");

        Add("form.dispatch.link.manual", "ارسال لینک فرم (دستی)", "form", "PenLine", "#10B981",
            "{customSmsBody}\n{link}",
            [
                P("customSmsBody", "متن دلخواه (می‌توانید از {firstName} و {lastName} استفاده کنید)", "سلام {firstName} {lastName}، لطفاً فرم را تکمیل کنید"),
                P("firstName", "نام", "علی"),
                P("lastName", "نام خانوادگی", "رضایی"),
                P("fullName", "نام کامل", "علی رضایی"),
                P("formTitle", "عنوان فرم", "فرم ارزیابی"),
                P("link", "لینک فرم", "https://example.com/f/abc"),
            ],
            "متن دستی + لینک در انتها");

        Add("exam.dispatch.link.default", "ارسال لینک آزمون", "exam", "Send", "#8B5CF6",
            "سلام {firstName} {lastName}\nآزمون «{examTitle}» برای شما فعال شد.\nشروع: {startDate} ساعت {startTime}\nپایان: {endDate} ساعت {endTime}\nلینک آزمون (بدون رمز):\n{link}",
            [
                P("firstName", "نام", "علی"),
                P("lastName", "نام خانوادگی", "رضایی"),
                P("fullName", "نام کامل", "علی رضایی"),
                P("examTitle", "عنوان آزمون", "آزمون فنی"),
                P("startDate", "تاریخ شروع (شمسی)", "۱۴۰۵/۰۱/۰۱"),
                P("startTime", "ساعت شروع", "۰۹:۰۰"),
                P("endDate", "تاریخ پایان (شمسی)", "۱۴۰۵/۰۱/۰۱"),
                P("endTime", "ساعت پایان", "۱۱:۰۰"),
                P("startAt", "شروع — تاریخ و ساعت یکجا", "۱۴۰۵/۰۱/۰۱ ۰۹:۰۰"),
                P("endAt", "پایان — تاریخ و ساعت یکجا", "۱۴۰۵/۰۱/۰۱ ۱۱:۰۰"),
                P("link", "لینک آزمون", "https://example.com/exams/fill?c=abc"),
            ],
            "ارسال گروهی لینک آزمون بدون OTP — زمان‌ها شمسی با ارقام فارسی");

        Add("form.submission.tracking.responder", "کد پیگیری پس از ثبت", "form", "Hash", "#14B8A6",
            "{honorificName} محترم،\n\nفرم «{formTitle}» با موفقیت ثبت شد.\nکد پیگیری شما: {trackingDisplay}\n\nلطفاً این کد را نگه دارید.",
            [
                P("honorificName", "نام با احترام", "آقای رضایی"),
                P("formTitle", "عنوان فرم", "فرم ارزیابی"),
                P("trackingDisplay", "کد پیگیری", "۱۲۳۴۵۶۷۸"),
            ]);

        Add("form.workflow.started.responder", "شروع گردش — به پاسخگو", "form", "GitBranch", "#8B5CF6",
            "{honorificName} گرامی،\n\nفرم «{formTitle}»{trackingPart}\nدر تاریخ {dateStr} ساعت {timeStr}\nدر سیکل جریان اداری قرار گرفت.\n\nدر صورت نیاز به پیگیری با شماره کد مراجعه فرمایید.",
            [
                P("honorificName", "نام با احترام", "آقای رضایی"),
                P("formTitle", "عنوان فرم", "فرم ارزیابی"),
                P("trackingPart", "بخش پیگیری (اختیاری)", "\nشماره پیگیری: ABC123"),
                P("dateStr", "تاریخ", "1404/03/08"),
                P("timeStr", "ساعت", "14:30"),
            ]);

        Add("form.workflow.completed.sender", "تأیید نهایی — به ارسال‌کننده", "form", "CheckCircle2", "#059669",
            "کارشناس گرامی،\n\nفرم «{formTitle}» به نام {honorificName} در تاریخ {dateStr} ساعت {timeStr} تأیید نهایی شد.{assigneeLine}\n\nمشاهده پرونده:\n{viewUrl}",
            [
                P("formTitle", "عنوان فرم"),
                P("honorificName", "نام پاسخگو"),
                P("dateStr", "تاریخ"),
                P("timeStr", "ساعت"),
                P("assigneeLine", "خط ارجاع اقدام (اختیاری)"),
                P("viewUrl", "لینک مشاهده"),
            ]);

        Add("form.action.completed.sender", "اتمام اقدام — به ارسال‌کننده", "form", "ClipboardCheck", "#059669",
            "کارشناس گرامی،\n\nفرم «{formTitle}» (پاسخگو: {honorificName})\nدر تاریخ {dateStr} ساعت {timeStr} در مرحله «{directionLabel}» وضعیت «انجام شده» ثبت شد.\nاقدام‌کننده: {actorLabel}{noteBlock}\n\nمشاهده پرونده:\n{viewUrl}",
            [
                P("formTitle", "عنوان فرم"),
                P("honorificName", "نام پاسخگو"),
                P("dateStr", "تاریخ"),
                P("timeStr", "ساعت"),
                P("directionLabel", "جهت اقدام"),
                P("actorLabel", "نام اقدام‌کننده"),
                P("noteBlock", "توضیحات (اختیاری)"),
                P("viewUrl", "لینک مشاهده"),
            ]);

        Add("form.responder.workflow.status", "وضعیت گردش — به پاسخگو", "form", "UserCheck", "#8B5CF6",
            "کاربر گرامی {honorificName}،\n\nگردش کاری شما {statusPhrase}.\nفرم «{formTitle}»\n{extraLines}\n\nدر صورت نیاز با واحد مربوطه تماس بگیرید.",
            [
                P("honorificName", "نام با احترام"),
                P("statusPhrase", "عبارت وضعیت", "در مرحله «انجام شده» قرار گرفت"),
                P("formTitle", "عنوان فرم"),
                P("extraLines", "خطوط اضافه (اختیاری)", "تاریخ: 1404/03/08 — ساعت 14:30"),
            ]);

        Add("form.workflow.reject.awaiting.sender", "رد — اقدام ارسال‌کننده", "form", "XCircle", "#E11D48",
            "رد درخواست فرم\nفرم «{formTitle}» — {honorificName}\nتوسط {rejecterLabel} رد شد ({dateStr} {timeStr}).{commentBlock}\n\nلینک فوری اقدام:\n{actionUrl}\n\n«درخواست مجدد تأیید» یا «اتمام گردش» (بایگانی).",
            [
                P("formTitle", "عنوان فرم"),
                P("honorificName", "نام پاسخگو"),
                P("rejecterLabel", "ردکننده"),
                P("dateStr", "تاریخ"),
                P("timeStr", "ساعت"),
                P("commentBlock", "یادداشت (اختیاری)"),
                P("actionUrl", "لینک اقدام"),
            ]);

        Add("form.workflow.reject.rejecter.confirm", "رد — تأیید به ردکننده", "form", "AlertTriangle", "#F59E0B",
            "ثبت رد درخواست\nفرم «{formTitle}» — {honorificName} را رد کردید.{commentBlock}\n\nدر صورت درخواست مجدد از طرف ارسال‌کننده، پیامک تأیید فوری برای شما ارسال می‌شود.",
            [P("formTitle", "عنوان فرم"), P("honorificName", "نام پاسخگو"), P("commentBlock", "یادداشت (اختیاری)")]);

        Add("form.workflow.reject.responder", "رد — به پاسخگو", "form", "Ban", "#E11D48",
            "{honorificName} محترم،\nپاسخ شما در فرم «{formTitle}» رد شد (توسط {rejecterLabel}).{commentBlock}",
            [P("honorificName", "نام"), P("formTitle", "عنوان فرم"), P("rejecterLabel", "ردکننده"), P("commentBlock", "یادداشت (اختیاری)")]);

        Add("form.workflow.reject.urgent.reapprove", "درخواست مجدد — فوری", "form", "Zap", "#F59E0B",
            "درخواست مجدد تأیید — فوری\nارسال‌کننده فرم «{formTitle}» ({honorificName}) درخواست بررسی مجدد ثبت کرد.\nلینک فوری تأیید/رد:\n{approvePath}",
            [P("formTitle", "عنوان فرم"), P("honorificName", "نام پاسخگو"), P("approvePath", "لینک تأیید")]);

        Add("form.workflow.rejected.final.sender", "رد قطعی — ارسال‌کننده", "form", "Archive", "#BE123C",
            "رد قطعی فرم\nفرم «{formTitle}» — {honorificName}\nدر تاریخ {dateStr} ساعت {timeStr} رد شد.\nردکننده: {rejecterLabel} (مرحله {stepOrder}){commentBlock}\n\nپرونده به بایگانی منتقل شد.\nمشاهده:\n{viewUrl}\n\nبایگانی فرم‌ها:\n{archiveUrl}",
            [
                P("formTitle", "عنوان فرم"), P("honorificName", "نام پاسخگو"),
                P("dateStr", "تاریخ"), P("timeStr", "ساعت"),
                P("rejecterLabel", "ردکننده"), P("stepOrder", "شماره مرحله"),
                P("commentBlock", "یادداشت (اختیاری)"),
                P("viewUrl", "لینک مشاهده"), P("archiveUrl", "لینک بایگانی"),
            ]);

        Add("form.approval.assignee.new", "ارجاع تأیید — جدید", "form", "UserCheck", "#6366F1",
            "پاسخ جدید از فرم «{formTitle}» برای تأیید شما ارسال شد.\nارجاع‌دهنده: {sender}\nلینک تأیید: {linkPath}\nیا پنل: {adminWorkflowRuns}",
            [P("formTitle", "عنوان فرم"), P("sender", "ارجاع‌دهنده"), P("linkPath", "لینک تأیید"), P("adminWorkflowRuns", "لینک پنل")]);

        Add("form.approval.assignee.reminder", "یادآوری تأیید فرم", "form", "Bell", "#6366F1",
            "یادآوری: پاسخ فرم «{formTitle}» همچنان منتظر تأیید شماست.\nلینک تأیید: {linkPath}\nیا پنل: {adminWorkflowRuns}",
            [P("formTitle", "عنوان فرم"), P("linkPath", "لینک تأیید"), P("adminWorkflowRuns", "لینک پنل")]);

        Add("form.approval.newRequest.panel", "درخواست تأیید — پنل", "form", "Inbox", "#6366F1",
            "یک درخواست جدید از فرم «{formTitle}» برای تایید شما ثبت شد.\nلطفا به پنل مدیریت بخش تاییدیه‌ها مراجعه کنید.{adminLinkBlock}",
            [P("formTitle", "عنوان فرم"), P("adminLinkBlock", "لینک مستقیم (اختیاری)", "\nلینک مستقیم: https://example.com/admin/approvals")]);

        Add("form.approval.referred.chain", "ارجاع زنجیره‌ای تأیید", "form", "ArrowLeftRight", "#6366F1",
            "درخواست فرم «{formTitle}» توسط {senderName} تایید شد و برای شما ارجاع گردید.\nلطفا برای بررسی به پنل مدیریت بخش تاییدیه‌ها مراجعه کنید.{adminLinkBlock}",
            [P("formTitle", "عنوان فرم"), P("senderName", "تأییدکننده قبلی"), P("adminLinkBlock", "لینک مستقیم (اختیاری)")]);

        Add("form.postapproval.assignee", "فاز اقدام — مسئول", "form", "PlayCircle", "#059669",
            "{staffName} گرامی،\n\nفرم «{formTitle}» به نام {responderName} در تاریخ {dateStr} ساعت {timeStr} تأیید نهایی شد.\nجهت {directionLabel} برای شما ارجاع شد.\n\nلینک فوری اقدام (بدون نیاز به ورود):\n{actionPath}\n\nیا از پنل:\n{adminPath}",
            [
                P("staffName", "نام کارشناس"), P("formTitle", "عنوان فرم"), P("responderName", "نام پاسخگو"),
                P("dateStr", "تاریخ"), P("timeStr", "ساعت"), P("directionLabel", "جهت اقدام"),
                P("actionPath", "لینک اقدام"), P("adminPath", "لینک پنل"),
            ]);

        Add("form.reminder.deadline", "تأخیر تأیید فرم", "form", "Timer", "#0EA5E9",
            "تأخیر در تأیید: مهلت ({deadlineLabel}) برای پاسخ فرم «{formTitle}» به پایان رسیده و هنوز تأیید شما ثبت نشده است.\nلینک تأیید: {linkPath}\nیا پنل: {adminApprovals}",
            [P("deadlineLabel", "برچسب مهلت"), P("formTitle", "عنوان فرم"), P("linkPath", "لینک تأیید"), P("adminApprovals", "لینک پنل")]);

        // ── contract ──
        Add("contract.approval.assignee.new", "ارجاع تأیید قرارداد", "contract", "FileSignature", "#D97706",
            "قرارداد شماره «{contractNumber}» برای تأیید شما ارسال شد.\nارجاع‌دهنده: {sender}\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}",
            [P("contractNumber", "شماره قرارداد"), P("sender", "ارجاع‌دهنده"), P("linkPath", "لینک تأیید")]);

        Add("contract.approval.assignee.reminder", "یادآوری تأیید قرارداد", "contract", "Bell", "#D97706",
            "یادآوری: قرارداد شماره «{contractNumber}» همچنان منتظر تأیید شماست.\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}",
            [P("contractNumber", "شماره قرارداد"), P("linkPath", "لینک تأیید")]);

        Add("contract.creator.step.approved", "تأیید مرحله — به ثبت‌کننده", "contract", "CheckCircle", "#D97706",
            "قرارداد شماره «{contractNumber}» با موضوع «{subject}»:\n{approverLabel} در تاریخ {dateStr} ساعت {timeStr} تأیید کرد.\n{statusTail}",
            [P("contractNumber", "شماره"), P("subject", "موضوع"), P("approverLabel", "تأییدکننده"), P("dateStr", "تاریخ"), P("timeStr", "ساعت"), P("statusTail", "ادامه وضعیت")]);

        Add("contract.amendment.assignee", "مسئول اصلاحیه", "contract", "FilePen", "#F59E0B",
            "قرارداد «{contractNumber}» توسط {rejecterName} رد شد ({rejectionTypeLabel}).\n{targetRole} باید اصلاحیه را انجام دهید.\nلینک اصلاح قرارداد:\n{amendPath}",
            [P("contractNumber", "شماره"), P("rejecterName", "ردکننده"), P("rejectionTypeLabel", "نوع رد"), P("targetRole", "نقش مسئول"), P("amendPath", "لینک اصلاح")]);

        Add("contract.amendment.return.rejecter", "بازگشت به ردکننده", "contract", "RotateCcw", "#F59E0B",
            "قرارداد «{contractNumber}» — اصلاحیه ({rejectionTypeLabel}) ارسال شد.\n{versionLine}\nلطفاً نسخه اصلاح‌شده را بررسی و تأیید یا رد کنید.\nلینک تأیید در پیامک ارجاع بعدی ارسال می‌شود.",
            [P("contractNumber", "شماره"), P("rejectionTypeLabel", "نوع"), P("versionLine", "نسخه (اختیاری)")]);

        Add("contract.rejection.notify.creator", "اطلاع رد به ثبت‌کننده", "contract", "AlertCircle", "#E11D48",
            "قرارداد «{contractNumber}» در مرحله {stepOrder} توسط {rejecterName} رد شد.\nنوع: {rejectionTypeLabel}{commentBlock}\nاصلاحیه در جریان است.",
            [P("contractNumber", "شماره"), P("stepOrder", "مرحله"), P("rejecterName", "ردکننده"), P("rejectionTypeLabel", "نوع"), P("commentBlock", "یادداشت (اختیاری)")]);

        Add("contract.postapproval.assignee", "فاز اقدام قرارداد", "contract", "Play", "#D97706",
            "قرارداد شماره «{contractNumber}» با موضوع «{subject}» جهت اقدام ({directionLabel}) برای شما ارسال شد.\nمشاهده گردش تأیید و ثبت وضعیت:\n{actionPath}\nیا از پنل: {adminPath}",
            [P("contractNumber", "شماره"), P("subject", "موضوع"), P("directionLabel", "جهت"), P("actionPath", "لینک"), P("adminPath", "پنل")]);

        Add("contract.action.completed.creator", "اتمام کار — ایجادکننده", "contract", "BadgeCheck", "#059669",
            "اتمام کار فاز اقدام قرارداد\nشماره قرارداد: {contractNumber}\nنوع قرارداد: {contractTypeName}\nموضوع: {subject}\nجهت اقدام: {directionLabel}\nاقدام‌کننده: {actorLabel}\nزمان ثبت: {dateStr} ساعت {timeStr}{noteBlock}\n\nمشاهده پرونده:\n{viewPath}\nپرونده به بایگانی منتقل شد.",
            [
                P("contractNumber", "شماره"), P("contractTypeName", "نوع"), P("subject", "موضوع"),
                P("directionLabel", "جهت"), P("actorLabel", "اقدام‌کننده"),
                P("dateStr", "تاریخ"), P("timeStr", "ساعت"), P("noteBlock", "توضیح (اختیاری)"), P("viewPath", "لینک"),
            ]);

        Add("contract.reminder.deadline", "تأخیر تأیید قرارداد", "contract", "Timer", "#0EA5E9",
            "تأخیر در تأیید: مهلت ({deadlineLabel}) برای قرارداد «{contractNumber}» به پایان رسیده و هنوز تأیید شما ثبت نشده است.\nلطفاً هرچه سریع‌تر بررسی کنید.\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}",
            [P("deadlineLabel", "مهلت"), P("contractNumber", "شماره"), P("linkPath", "لینک")]);

        Add("contract.reminder.workflowValidity", "یادآوری اعتبار گردش", "contract", "Hourglass", "#E11D48",
            "یادآوری: قرارداد شماره «{contractNumber}» با موضوع «{subject}» هنوز توسط شما امضا/تأیید نشده است.\nاعتبار گردش کار به پایان رسیده و از مهلت مقرر تأخیر دارید.\nلینک سریع امضا و تأیید:\n{linkPath}",
            [P("contractNumber", "شماره"), P("subject", "موضوع"), P("linkPath", "لینک")]);

        Add("contract.party.final.approved", "تأیید نهایی — طرف قرارداد", "contract", "Award", "#059669",
            "قرارداد شما با شماره سند «{contractNumber}» در تاریخ {dateStr} ساعت {timeStr} تأیید شد.",
            [P("contractNumber", "شماره"), P("dateStr", "تاریخ"), P("timeStr", "ساعت")]);

        Add("contract.rejection.full.final", "رد قطعی قرارداد", "contract", "XOctagon", "#BE123C",
            "{recipientName}\nقرارداد «{contractNumber}» با رد کامل قطعی پایان یافت و بایگانی شد.\nمرحله رد: {stepOrder} — {stepUserName}{commentLine}\nمشاهده نتیجه (لینک فوری):\n{linkPath}",
            [P("recipientName", "گیرنده"), P("contractNumber", "شماره"), P("stepOrder", "مرحله"), P("stepUserName", "ردکننده"), P("commentLine", "یادداشت (اختیاری)"), P("linkPath", "لینک")]);

        // ── document ──
        Add("document.approval.assignee.new", "ارجاع تأیید سند", "document", "FileText", "#6366F1",
            "سند «{docTitle}» برای تأیید شما ارسال شد.\nارجاع‌دهنده: {sender}\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}\nیا پنل: {adminWorkflowRuns}",
            [P("docTitle", "عنوان سند"), P("sender", "ارجاع‌دهنده"), P("linkPath", "لینک"), P("adminWorkflowRuns", "پنل")]);

        Add("document.approval.assignee.reminder", "یادآوری تأیید سند", "document", "Bell", "#6366F1",
            "یادآوری: سند «{docTitle}» همچنان منتظر تأیید شماست.\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}\nیا پنل: {adminWorkflowRuns}",
            [P("docTitle", "عنوان"), P("linkPath", "لینک"), P("adminWorkflowRuns", "پنل")]);

        Add("document.owner.step.approved", "تأیید مرحله — مالک سند", "document", "CheckCircle2", "#6366F1",
            "سند «{docTitle}»{refPart}:\n{approverLabel} در تاریخ {dateStr} ساعت {timeStr} تأیید کرد.\n{statusTail}",
            [P("docTitle", "عنوان"), P("refPart", "شماره ارجاع (اختیاری)"), P("approverLabel", "تأییدکننده"), P("dateStr", "تاریخ"), P("timeStr", "ساعت"), P("statusTail", "ادامه")]);

        Add("document.workflow.completed.owner", "تأیید نهایی سند", "document", "BadgeCheck", "#059669",
            "سند «{docTitle}»{refPart} در تمام مراحل تأیید شد و گردش به پایان رسید.",
            [P("docTitle", "عنوان"), P("refPart", "شماره ارجاع (اختیاری)")]);

        Add("document.workflow.rejected.owner", "رد گردش سند", "document", "XCircle", "#E11D48",
            "سند «{docTitle}»{refPart} در گردش تأیید رد شد.\nردکننده: {approverName}{note}",
            [P("docTitle", "عنوان"), P("refPart", "ارجاع (اختیاری)"), P("approverName", "ردکننده"), P("note", "توضیح (اختیاری)")]);

        Add("document.postapproval.assignee", "فاز اقدام سند", "document", "PlayCircle", "#6366F1",
            "{staffName} گرامی،\n\nسند «{docTitle}» تأیید نهایی شد.\nجهت {directionLabel} برای شما ارجاع شد.\n\nاز پنل:\n{adminPath}",
            [P("staffName", "نام"), P("docTitle", "عنوان"), P("directionLabel", "جهت"), P("adminPath", "پنل")]);

        Add("document.reminder.deadline", "تأخیر تأیید سند", "document", "Timer", "#0EA5E9",
            "تأخیر در تأیید: مهلت ({deadlineLabel}) برای سند «{docTitle}»{refPart} به پایان رسیده و هنوز تأیید شما ثبت نشده است.\nلینک تأیید (بدون نیاز به ورود):\n{linkPath}\nیا پنل: {adminWorkflowRuns}",
            [P("deadlineLabel", "مهلت"), P("docTitle", "عنوان"), P("refPart", "ارجاع (اختیاری)"), P("linkPath", "لینک"), P("adminWorkflowRuns", "پنل")]);

        return list;
    }

    public static readonly IReadOnlyDictionary<string, (string Title, string Icon, string Color)> CategoryMeta =
        new Dictionary<string, (string, string, string)>(StringComparer.OrdinalIgnoreCase)
        {
            ["auth"] = ("احراز هویت", "Shield", "#6366F1"),
            ["user"] = ("کاربران", "User", "#0EA5E9"),
            ["form"] = ("فرم و گردش", "FileText", "#10B981"),
            ["contract"] = ("قرارداد", "FileSignature", "#D97706"),
            ["document"] = ("اسناد", "FolderOpen", "#6366F1"),
        };
}
