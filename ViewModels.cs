using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AttandanceTracking.Models;

#nullable disable warnings

public class RegisterViewModel
{
    public string FullName { get; set; }
    public string Email { get; set; }

    // New property
    [Required]
    public Guid ClassId { get; set; }

    // For rendering dropdown
    public List<SelectListItem>? Classes { get; set; }
}
public class LoginViewModel
{
    public string Email { get; set; }
    public string Password { get; set; }
    public bool RememberMe { get; set; }
}

public class ResetPasswordViewModel
{
    [Required]
    public string UserId { get; set; }

    [Required]
    public string Token { get; set; }

    [Required]
    [DataType(DataType.Password)]
    [Display(Name = "New Password")]
    public string Password { get; set; }

    [DataType(DataType.Password)]
    [Display(Name = "Confirm Password")]
    [Compare("Password", ErrorMessage = "Passwords do not match.")]
    public string ConfirmPassword { get; set; }
}

public class ForgotPasswordViewModel
{
    [Required]
    [EmailAddress]
    public string Email { get; set; } = string.Empty;
}

public class EmailSettings
{
    public string SmtpServer { get; set; }
    public int Port { get; set; }
    public string SenderName { get; set; }
    public string SenderEmail { get; set; }
    public string Username { get; set; }
    public string Password { get; set; }
}
public class Captcha
{
    public string SiteKey { get; set; }
    public string SecretKey { get; set; }
}

public class ClassViewModel
{
    public Guid Id { get; set; }
    public string Code { get; set; }
    public string Description { get; set; }

    // Student selection
    public List<string> SelectedStudentIds { get; set; } = new List<string>();

    // For rendering the dropdown/multi-select
    public List<SelectListItem> AllStudents { get; set; } = new List<SelectListItem>();
}


public class AttendanceFilterVM
{
    [Display(Name = "Class")] public Guid? ClassId { get; set; }
    [Display(Name = "Subject")] public Guid? SubjectId { get; set; }

    [Display(Name = "From"), DataType(DataType.Date)]
    public DateTime? From { get; set; }

    [Display(Name = "To"), DataType(DataType.Date)]
    public DateTime? To { get; set; }
}

public class StudentAttendanceRowVM
{
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string? ClassCode { get; set; }
    public string? SubjectCode { get; set; }

    public int TotalSessions { get; set; }
    public int PresentCount { get; set; }
    public double Percentage => TotalSessions == 0 ? 0 : Math.Round(PresentCount * 100.0 / TotalSessions, 2);

    // Show schedule from the student's most-recent attended session in range
    public DateTime? StartTime { get; set; }
    public DateTime? EndTime { get; set; }
}

public class StudentAttendanceReportVM
{
    public AttendanceFilterVM Filter { get; set; } = new();
    public List<StudentAttendanceRowVM> Rows { get; set; } = new();
}