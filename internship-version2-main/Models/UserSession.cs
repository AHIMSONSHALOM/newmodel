using System.ComponentModel.DataAnnotations;

namespace ProductHub_MVC.Models
{
    public class UserSession
    {
        [Key]
        public Guid SessionId { get; set; }
        public int UserId { get; set; }
        public string Email { get; set; } = string.Empty;
        public string DeviceId { get; set; } = string.Empty;
        public string BrowserInfo { get; set; } = string.Empty;
        public string IpAddress { get; set; } = string.Empty;
        public DateTime LoginTime { get; set; }
        public DateTime? LogoutTime { get; set; }
        public bool IsActive { get; set; }
    }
}
