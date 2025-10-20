namespace Aegis.SDK.Models
{
    public record DatabaseChoice(
        string Provider,
        string ServerName,
        string DatabaseName,
        string UserName,
        string Password,
        string Port
    );
}
