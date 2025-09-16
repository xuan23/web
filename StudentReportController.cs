using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using AttandanceTracking.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AttandanceTracking.Controllers
{
    [Authorize(Roles = "Student")]
    public class StudentReportController : Controller
    {
        private readonly DB _db;
        private readonly UserManager<ApplicationUser> _userManager;

        // Adjust if your "present" value differs (e.g., enum)
        private const int PRESENT = 1;

        public StudentReportController(DB db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // GET: /StudentReport/MySummary
        [HttpGet]
        public async Task<IActionResult> MySummary(string? sort, string? dir)
        {
            // Defaults used by the view
            sort ??= "SubjectName";
            dir ??= "asc";
            bool asc = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase);

            var me = await _userManager.GetUserAsync(User);
            if (me == null) return Unauthorized();

            // If student has no class, return empty list
            if (me.ClassId == null)
            {
                ViewBag.Sort = sort;
                ViewBag.Dir = dir;
                return View("~/Views/StudentReport/MySummary.cshtml", new List<StudentSubjectSummaryRowVM>());
            }

            // All sessions for MY class that are tied to a subject (need Subject for name)
            var sessions = await _db.Sessions.AsNoTracking()
                .Include(s => s.Subject)
                .Where(s => s.ClassId == me.ClassId && s.SubjectId != null)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            if (sessions.Count == 0)
            {
                ViewBag.Sort = sort;
                ViewBag.Dir = dir;
                return View("~/Views/StudentReport/MySummary.cshtml", new List<StudentSubjectSummaryRowVM>());
            }

            var sessionIds = sessions.Select(s => s.Id).ToList();

            // Distinct present sessions for this student
            var presentIds = await _db.Attendances.AsNoTracking()
                .Where(a => a.StudentId == me.Id && sessionIds.Contains(a.SessionId) && a.Status == PRESENT)
                .Select(a => a.SessionId)
                .Distinct()
                .ToListAsync();
            var presentSet = new HashSet<Guid>(presentIds);

            // Totals per Subject (denominator)
            var totalsBySubject = sessions
                .Where(s => s.SubjectId != null)
                .GroupBy(s => s.SubjectId!.Value)
                .ToDictionary(g => g.Key, g => g.Count());

            // Build one row per Subject
            var rows = sessions
                .Where(s => s.SubjectId != null)
                .GroupBy(s => s.SubjectId!.Value)
                .Select(g =>
                {
                    var first = g.First();
                    var subjectName = (first.Subject?.Description ?? "-").Trim();
                    var subjectCode = (first.Subject?.Code ?? "-").Trim();

                    return new StudentSubjectSummaryRowVM
                    {
                        SubjectId = g.Key,
                        SubjectName = subjectName,      // used in the table
                        SubjectCode = subjectCode,      // not shown but OK to keep
                        // LecturerNames not needed; leave default
                        TotalSessions = totalsBySubject[g.Key],
                        PresentCount = g.Count(sess => presentSet.Contains(sess.Id))
                    };
                })
                .ToList();

            // Sorting: only SubjectName or Percentage are relevant now
            rows = sort switch
            {
                "Percentage" => (asc ? rows.OrderBy(r => r.Percentage)
                                     : rows.OrderByDescending(r => r.Percentage)).ToList(),
                _ => (asc ? rows.OrderBy(r => r.SubjectName)
                                     : rows.OrderByDescending(r => r.SubjectName)).ToList(),
            };

            // Pass sort state to the view
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            return View("~/Views/StudentReport/MySummary.cshtml", rows);
        }

        // GET: /StudentReport/StudentSessions?subjectId=...
        // Returns the session list for the selected subject (no Lecturer field used)
        [HttpGet]
        public async Task<IActionResult> StudentSessions(Guid subjectId)
        {
            var me = await _userManager.GetUserAsync(User);
            if (me == null) return Unauthorized();
            if (me.ClassId == null)
                return PartialView("~/Views/Report/_StudentSessions.cshtml", new List<SessionAttendanceDetailVM>());

            var sessions = await _db.Sessions.AsNoTracking()
                .Include(s => s.Subject)
                // .Include(s => s.Lecturer) // not needed if popup doesn't show lecturer
                .Where(s => s.ClassId == me.ClassId && s.SubjectId == subjectId)
                .OrderBy(s => s.StartTime)
                .ToListAsync();

            var ids = sessions.Select(s => s.Id).ToList();

            var myAtt = await _db.Attendances.AsNoTracking()
                .Where(a => a.StudentId == me.Id && ids.Contains(a.SessionId))
                .Select(a => new { a.SessionId, a.Status })
                .ToListAsync();

            var details = sessions.Select(sess =>
            {
                bool present = myAtt.Any(a => a.SessionId == sess.Id && a.Status == PRESENT);

                // Subject label: "CODE - NAME" when both exist, else fallback
                string label = "-";
                if (sess.Subject != null)
                {
                    var code = (sess.Subject.Code ?? "").Trim();
                    var name = (sess.Subject.Description ?? "").Trim();
                    label = (string.IsNullOrWhiteSpace(code), string.IsNullOrWhiteSpace(name)) switch
                    {
                        (true, true) => "-",
                        (false, true) => code,
                        (true, false) => name,
                        _ => $"{code} - {name}"
                    };
                }

                return new SessionAttendanceDetailVM
                {
                    SubjectName = label,
                    // LecturerName = ... // not used
                    StartTime = sess.StartTime,
                    EndTime = sess.EndTime,
                    Status = present ? "Present" : "Absent"
                };
            }).ToList();

            return PartialView("~/Views/Report/_StudentSessions.cshtml", details);
        }
    }
}
