namespace PorslineClone.Infrastructure.Services;

public static class ContractWorkflowRejectionTypes
{
    public const string Full = "full";
    public const string ContractAmendment = "contract_amendment";
    public const string NeedsMeeting = "needs_meeting";

    public static string Normalize(string? input) => input switch
    {
        Full => Full,
        ContractAmendment or "amend_contract" => ContractAmendment,
        NeedsMeeting or "meeting" => NeedsMeeting,
        _ => ContractAmendment
    };

    public static string Label(string? type) => type switch
    {
        Full => "رد کامل",
        ContractAmendment => "اصلاح قرارداد",
        NeedsMeeting => "برگزاری جلسه",
        _ => "اصلاح / جلسه"
    };
}
