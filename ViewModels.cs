using Microsoft.AspNetCore.Mvc.Rendering;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace AttandanceTracking.Models;

#nullable disable warnings

public class RegisterViewModel
{
    public string? Id { get; set; }

    [Required]
    public string FullName { get; set; }

    [Required, EmailAddress]
    public string Email { get; set; }

    public Guid? ClassId { get; set; }
    public List<SelectListItem>? Classes { get; set; }

    public List<Guid> SelectedSubjectIds { get; set; } = new List<Guid>();
    public List<SelectListItem>? Subjects { get; set; }
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

// existing viewmodels ...

// Filters coming from the ReportView filter bar
public class AttendanceFilterVM
{
    public Guid? ClassId { get; set; }
    public Guid? SubjectId { get; set; }
    [DataType(DataType.Date)] public DateTime? From { get; set; }
    [DataType(DataType.Date)] public DateTime? To { get; set; }
}

// One row in the summary table
public class StudentAttendanceRowVM
{
    public string StudentId { get; set; } = "";
    public string StudentName { get; set; } = "";
    public string? StudentEmail { get; set; }

    public string? ClassCode { get; set; }
    public string? ClassName { get; set; }

    public string? SubjectCode { get; set; }
    public string? SubjectName { get; set; }

    public int TotalSessions { get; set; }
    public int PresentCount { get; set; }

    public double Percentage => TotalSessions > 0 ? (PresentCount * 100.0 / TotalSessions) : 0.0;
}

// Page VM for ReportView
public class StudentAttendanceReportVM
{
    public AttendanceFilterVM Filter { get; set; } = new();
    public List<StudentAttendanceRowVM> Rows { get; set; } = new();
}

// Detail rows for the modal partial
public partial class SessionAttendanceDetailVM
{
    public string? SubjectName { get; set; }
    public DateTime StartTime { get; set; } // non-nullable: matches the partial formatting
    public DateTime EndTime { get; set; } // non-nullable
    public string Status { get; set; } = "Absent"; // "Present" or "Absent"
}


public class StudentSubjectSummaryRowVM
{
    public Guid SubjectId { get; set; }
    public string SubjectCode { get; set; } = "-";
    public string SubjectName { get; set; } = "Overall";
    public string LecturerNames { get; set; } = "-"; // comma-separated

    public int TotalSessions { get; set; }   // denominator
    public int PresentCount { get; set; }    // distinct sessions student was present

    public double Percentage => TotalSessions > 0 ? (PresentCount * 100.0 / TotalSessions) : 0.0;
}
