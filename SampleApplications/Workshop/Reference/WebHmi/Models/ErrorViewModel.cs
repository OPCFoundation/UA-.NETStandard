using System;

namespace WebHmi.Models
{
    public class ErrorViewModel
    {
        public string RequestId { get; set; }

        public string ErrorCode { get; set; }

        public string ErrorMessage { get; set; }

        public bool ShowRequestId => !string.IsNullOrEmpty(RequestId);
    }
}
