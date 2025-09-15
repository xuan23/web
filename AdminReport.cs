using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using AttandanceTracking.Models;

namespace AttandanceTracking.Controllers
{
    public class StudentAttendancesReport : Controller
    {
        private readonly DB _db;
        public StudentAttendancesReport(DB db) => _db = db;

        // GET: /Attendances/ReportView
        [HttpGet]
        public async Task<IActionResult> ReportView(AttendanceFilterVM filter)
        {
            filter ??= new AttendanceFilterVM();
            const int PRESENT = 1; // change if your "present" status differs

            var classesQ = _db.Classes.AsNoTracking();
            var subjectsQ = _db.Subjects.AsNoTracking();
            var sessionsQ = _db.Sessions.AsNoTracking();
            var usersQ = _db.Users.AsNoTracking();

            // Use StartTime/EndTime from Session (your model no longer has a generic 'Time')
            if (filter.ClassId.HasValue) sessionsQ = sessionsQ.Where(s => s.ClassId == filter.ClassId);
            if (filter.SubjectId.HasValue) sessionsQ = sessionsQ.Where(s => s.SubjectId == filter.SubjectId);
            if (filter.From.HasValue) sessionsQ = sessionsQ.Where(s => s.StartTime >= filter.From.Value.Date);
            if (filter.To.HasValue) sessionsQ = sessionsQ.Where(s => s.StartTime < filter.To.Value.Date.AddDays(1));

            var sessionList = await sessionsQ
                .Include(s => s.Class)
                .Include(s => s.Subject)
                .ToListAsync();

            var sessionIds = sessionList.Select(s => s.Id).ToList();
            var totalSessions = sessionList.Count;

            // --- Students in scope ---------------------------------------------------------
            var studentsQ = usersQ.AsQueryable();
            if (filter.ClassId.HasValue)
            {
                studentsQ = studentsQ.Where(u => u.ClassId == filter.ClassId);
            }
            else if (sessionList.Any())
            {
                var classIds = sessionList.Where(s => s.ClassId != null)
                                          .Select(s => s.ClassId!.Value)
                                          .Distinct()
                                          .ToList();
                studentsQ = studentsQ.Where(u => u.ClassId != null && classIds.Contains(u.ClassId!.Value));
            }

            var students = await studentsQ
                .Include(u => u.Class)
                .OrderBy(u => u.FullName)
                .ToListAsync();

            // --- Attendance aggregates -----------------------------------------------------
            var presentByStudent = await _db.Attendances.AsNoTracking()
                .Where(a => sessionIds.Contains(a.SessionId) && a.Status == PRESENT)
                .GroupBy(a => a.StudentId)
                .Select(g => new { StudentId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.StudentId, x => x.Count);

            // Join Attendance -> Session to read session schedule & subject for each attended record,
            // then pick the most-recent attended session per student inside the filtered range.
            var attWithSession = await _db.Attendances.AsNoTracking()
                .Where(a => sessionIds.Contains(a.SessionId))
                .Join(_db.Sessions.AsNoTracking(),
                      a => a.SessionId,
                      s => s.Id,
                      (a, s) => new { a.StudentId, a.ScanTime, s.StartTime, s.EndTime, s.SubjectId })
                .ToListAsync();

            var lastAttendedSessionByStudent = attWithSession
                .GroupBy(x => x.StudentId)
                .ToDictionary(g => g.Key,
                              g => g.OrderByDescending(x => x.ScanTime).First());

            // --- Labels / Display codes ----------------------------------------------------
            string? classCode = null, subjectCode = null;

            if (filter.ClassId.HasValue)
            {
                classCode = await classesQ
                    .Where(c => c.Id == filter.ClassId.Value)
                    .Select(c => c.Code)
                    .FirstOrDefaultAsync();
            }

            if (filter.SubjectId.HasValue)
            {
                // User explicitly chose a subject
                subjectCode = await subjectsQ
                    .Where(s => s.Id == filter.SubjectId.Value)
                    .Select(s => s.Code)
                    .FirstOrDefaultAsync();
            }
            else
            {
                // Auto-pick subject if all sessions in the range share the same subject
                var subjectIdsInRange = sessionList
                    .Where(s => s.SubjectId != null)
                    .Select(s => s.SubjectId!.Value)
                    .Distinct()
                    .ToList();

                if (subjectIdsInRange.Count == 1)
                {
                    var onlyId = subjectIdsInRange[0];
                    subjectCode = await subjectsQ
                        .Where(s => s.Id == onlyId)
                        .Select(s => s.Code)
                        .FirstOrDefaultAsync();
                }
            }

            // For per-student fallback when multiple subjects exist in range
            var subjectCodeById = await subjectsQ
                .ToDictionaryAsync(s => s.Id, s => s.Code);

            var rows = students.Select(stu =>
            {
                presentByStudent.TryGetValue(stu.Id, out var presentCount);
                lastAttendedSessionByStudent.TryGetValue(stu.Id, out var last);

                // Prefer the single detected subject for the page, else fall back to student's last attended subject
                string? rowSubjectCode = subjectCode;
                if (rowSubjectCode == null && last?.SubjectId != null &&
                    subjectCodeById.TryGetValue(last.SubjectId.Value, out var fromLast))
                {
                    rowSubjectCode = fromLast;
                }

                return new StudentAttendanceRowVM
                {
                    StudentId = stu.Id,
                    StudentName = string.IsNullOrWhiteSpace(stu.FullName) ? (stu.Email ?? stu.Id) : stu.FullName,
                    ClassCode = classCode ?? stu.Class?.Code,
                    SubjectCode = rowSubjectCode,
                    TotalSessions = totalSessions,
                    PresentCount = presentCount,
                    // Show the schedule from the student's most-recent attended session in range
                    StartTime = last?.StartTime,
                    EndTime = last?.EndTime
                };
            })
            .OrderByDescending(r => r.Percentage)
            .ThenBy(r => r.StudentName)
            .ToList();

            ViewBag.Classes = await classesQ.OrderBy(c => c.Code).ToListAsync();
            ViewBag.Subjects = await subjectsQ.OrderBy(s => s.Code).ToListAsync();
            ViewBag.TotalSessions = totalSessions;

            return View(new StudentAttendanceReportVM { Filter = filter, Rows = rows });
        }
    }
}
