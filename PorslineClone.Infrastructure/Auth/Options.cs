namespace PorslineClone.Infrastructure.Auth;

public class JwtOptions
{
    public const string SectionName = "Jwt";
    public string Issuer { get; set; } = "PorslineClone";
    public string Audience { get; set; } = "PorslineCloneClient";
    public string Key { get; set; } = "YourVeryStrongJwtSecretKeyAtLeast32Chars!";
    public int ExpMinutes { get; set; } = 180;
}

public class SmsGatewayOptions
{
    public const string SectionName = "SmsGateway";
    public string UrlAddress { get; set; } = "https://api2.entekhabservice.ir/SendDirectSms";
    public string CallerId { get; set; } = "CRM-WEB-TOOLS";
    public string Password { get; set; } = "CHANGE_ME";
}
