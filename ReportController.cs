using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AttandanceTracking.Models;

namespace AttandanceTracking.Controllers
{
    [Authorize(Roles = "Admin,Lecturer")]
    public class ReportController : Controller
    {
        private readonly DB _db;
        private readonly UserManager<ApplicationUser> _userManager;

        public ReportController(DB db, UserManager<ApplicationUser> userManager)
        {
            _db = db;
            _userManager = userManager;
        }

        // MAIN PAGE + AJAX sorting
        [HttpGet]
        public async Task<IActionResult> ReportView(AttendanceFilterVM filter, string? search, string? sort, string? dir)
        {
            filter ??= new AttendanceFilterVM();
            ViewBag.Search = search = (search ?? string.Empty).Trim();
            ViewBag.Sort = sort;
            ViewBag.Dir = dir;

            var isLecturer = User.IsInRole("Lecturer");

            // Get current lecturer id once (no await inside LINQ)
            string? lecturerId = null;
            if (isLecturer)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();
                lecturerId = user.Id;
            }

            // Subject scope by role
            var mySubjectIds = isLecturer
                ? await _db.LecturerSubjects
                    .Where(ls => ls.LecturerId == lecturerId)
                    .Select(ls => ls.SubjectId)
                    .ToListAsync()
                : await _db.Subjects.AsNoTracking().Select(s => s.Id).ToListAsync();

            // Sessions within filters
            IQueryable<Session> sessionsQ = _db.Sessions.AsNoTracking()
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .Where(s => s.SubjectId != null && mySubjectIds.Contains(s.SubjectId.Value));

            if (filter.ClassId.HasValue)
                sessionsQ = sessionsQ.Where(s => s.ClassId == filter.ClassId.Value);
            if (filter.SubjectId.HasValue)
                sessionsQ = sessionsQ.Where(s => s.SubjectId == filter.SubjectId.Value);
            if (filter.From.HasValue)
                sessionsQ = sessionsQ.Where(s => s.StartTime >= filter.From.Value.Date);
            if (filter.To.HasValue)
                sessionsQ = sessionsQ.Where(s => s.StartTime < filter.To.Value.Date.AddDays(1));

            var sessionList = await sessionsQ.OrderBy(s => s.StartTime).ToListAsync();
            var sessionIds = sessionList.Select(s => s.Id).ToList();

            // Distinct PRESENT counts per student (one per SessionId)
            const int PRESENT = 1;
            var presentByStudent = await _db.Attendances.AsNoTracking()
                .Where(a => sessionIds.Contains(a.SessionId) && a.Status == PRESENT)
                .Select(a => new { a.StudentId, a.SessionId })
                .Distinct()
                .GroupBy(x => x.StudentId)
                .Select(g => new { StudentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StudentId, x => x.Count);

            // Totals per (Class,Subject) for denominators
            var totalsByClassSubject = sessionList
                .Where(s => s.ClassId != null && s.SubjectId != null)
                .GroupBy(s => (s.ClassId!.Value, s.SubjectId!.Value))
                .ToDictionary(g => g.Key, g => g.Count());

            // Classes in scope -> students (+ optional search on name/email)
            var classIdsInScope = sessionList.Where(s => s.ClassId != null)
                                             .Select(s => s.ClassId!.Value)
                                             .Distinct().ToList();

            var studentsQ = _db.Users.AsNoTracking()
                .Include(u => u.Class) // change to .Include(u => u.Classes) if your nav is plural
                .Where(u => u.ClassId != null && classIdsInScope.Contains(u.ClassId.Value));

            if (!string.IsNullOrEmpty(search))
                studentsQ = studentsQ.Where(u => u.FullName.Contains(search) || u.Email.Contains(search));

            var students = await studentsQ.OrderBy(u => u.FullName).ToListAsync();

            var subjectById = await _db.Subjects.AsNoTracking()
                .Where(s => mySubjectIds.Contains(s.Id))
                .ToDictionaryAsync(s => s.Id, s => new { s.Code, s.Description });

            // Build rows for the table
            var rows = students.Select(stu =>
            {
                presentByStudent.TryGetValue(stu.Id, out var presentDistinct);

                int totalForStudent = 0;
                string? subjCode = null;
                string? subjName = null;

                if (!filter.SubjectId.HasValue)
                {
                    // Overall across subjects (denominator = all sessions for the student's class within date range)
                    if (stu.ClassId != null)
                        totalForStudent = sessionList.Count(s => s.ClassId == stu.ClassId);
                    subjCode = "-";
                    subjName = "Overall";
                }
                else
                {
                    if (stu.ClassId != null &&
                        totalsByClassSubject.TryGetValue((stu.ClassId.Value, filter.SubjectId.Value), out var ts))
                        totalForStudent = ts;

                    if (subjectById.TryGetValue(filter.SubjectId.Value, out var subj))
                    {
                        subjCode = subj.Code;
                        subjName = subj.Description;
                    }
                }

                var cappedPresent = Math.Min(presentDistinct, totalForStudent);

                return new StudentAttendanceRowVM
                {
                    StudentId = stu.Id,
                    StudentName = string.IsNullOrWhiteSpace(stu.FullName) ? (stu.Email ?? stu.Id) : stu.FullName,
                    StudentEmail = stu.Email,
                    ClassCode = stu.Class?.Code,
                    ClassName = stu.Class?.Description,
                    SubjectCode = subjCode,
                    SubjectName = subjName,
                    TotalSessions = totalForStudent,
                    PresentCount = cappedPresent
                };
            }).ToList();

            // Apply sorting on in-memory rows
            var ascending = string.Equals(dir, "asc", StringComparison.OrdinalIgnoreCase) || string.IsNullOrEmpty(dir);
            rows = (sort ?? "StudentName") switch
            {
                "Percentage" => (ascending ? rows.OrderBy(r => r.Percentage) : rows.OrderByDescending(r => r.Percentage)).ToList(),
                "StudentName" => (ascending ? rows.OrderBy(r => r.StudentName) : rows.OrderByDescending(r => r.StudentName)).ToList(),
                _ => rows.OrderBy(r => r.StudentName).ToList()
            };

            // Non-AJAX: return full page
            if (Request.Headers["X-Requested-With"] != "XMLHttpRequest")
            {
                ViewBag.Classes = await _db.Classes.AsNoTracking()
                    .Where(c => classIdsInScope.Contains(c.Id))
                    .OrderBy(c => c.Code)
                    .ToListAsync();

                ViewBag.Subjects = await _db.Subjects.AsNoTracking()
                    .Where(s => mySubjectIds.Contains(s.Id))
                    .OrderBy(s => s.Code)
                    .ToListAsync();

                return View("~/Views/Report/ReportView.cshtml", new StudentAttendanceReportVM
                {
                    Filter = filter,
                    Rows = rows
                });
            }

            // AJAX: return table-only partial
            return PartialView("~/Views/Report/_ReportTable.cshtml", rows);
        }

        // MODAL PARTIAL (opens from “x / y” link)
        [HttpGet]
        public async Task<IActionResult> StudentSessions(string studentId, Guid? classId, Guid? subjectId, DateTime? from, DateTime? to)
        {
            var isLecturer = User.IsInRole("Lecturer");

            string? lecturerId = null;
            if (isLecturer)
            {
                var user = await _userManager.GetUserAsync(User);
                if (user == null) return Unauthorized();
                lecturerId = user.Id;
            }

            var mySubjectIds = isLecturer
                ? await _db.LecturerSubjects.Where(ls => ls.LecturerId == lecturerId)
                    .Select(ls => ls.SubjectId).ToListAsync()
                : await _db.Subjects.AsNoTracking().Select(s => s.Id).ToListAsync();

            IQueryable<Session> sessionsQ = _db.Sessions.AsNoTracking()
                .Include(s => s.Subject)
                .Where(s => s.SubjectId != null && mySubjectIds.Contains(s.SubjectId.Value));

            if (classId.HasValue) sessionsQ = sessionsQ.Where(s => s.ClassId == classId.Value);
            if (subjectId.HasValue) sessionsQ = sessionsQ.Where(s => s.SubjectId == subjectId.Value);
            if (from.HasValue) sessionsQ = sessionsQ.Where(s => s.StartTime >= from.Value.Date);
            if (to.HasValue) sessionsQ = sessionsQ.Where(s => s.StartTime < to.Value.Date.AddDays(1));

            var sessions = await sessionsQ.OrderBy(s => s.StartTime).ToListAsync();
            var sessionIds = sessions.Select(s => s.Id).ToList();

            var attendances = await _db.Attendances.AsNoTracking()
                .Where(a => a.StudentId == studentId && sessionIds.Contains(a.SessionId))
                .ToListAsync();

            var details = sessions.Select(sess =>
            {
                var present = attendances.Any(a => a.SessionId == sess.Id && a.Status == 1);

                // Build "CODE - NAME" label (gracefully handles missing parts)
                string? label = "-";
                if (sess.Subject != null)
                {
                    var code = (sess.Subject.Code ?? "").Trim();
                    var name = (sess.Subject.Description ?? "").Trim();
                    label = (string.IsNullOrWhiteSpace(code) && string.IsNullOrWhiteSpace(name)) ? "-"
                          : string.IsNullOrWhiteSpace(code) ? name
                          : string.IsNullOrWhiteSpace(name) ? code
                          : $"{code} - {name}";
                }

                return new SessionAttendanceDetailVM
                {
                    SubjectName = label,
                    StartTime = sess.StartTime,
                    EndTime = sess.EndTime,
                    Status = present ? "Present" : "Absent"
                };
            }).ToList();

            return PartialView("~/Views/Report/_StudentSessions.cshtml", details);
        }
    }
}
