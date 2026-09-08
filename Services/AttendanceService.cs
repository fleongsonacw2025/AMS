using AMS.Data;
using AMS.Models;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AMS.Services;

public class AttendanceService
{
    private readonly AmsDbContext _db;
    public User? CurrentUser { get; private set; }
    public event Action? OnNotify;

    public AttendanceService(AmsDbContext db) => _db = db;

    // Authentication & User Management
    public async Task<User?> Login(string email, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.Password == password);
        if (user != null) { CurrentUser = user; OnNotify?.Invoke(); }
        return user;
    }

    public void Logout() { CurrentUser = null; OnNotify?.Invoke(); }

    public async Task<List<User>> GetAllUsers() => await _db.Users.ToListAsync();
    public async Task<List<Site>> GetSites() => await _db.Sites.ToListAsync();

    public async Task SaveUser(User user)
    {
        if (user.Id == 0) _db.Users.Add(user);
        else _db.Users.Update(user);
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    public async Task DeleteUser(int userId)
    {
        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            _db.Users.Remove(user);
            _db.AuditLogs.Add(new AuditLog 
            { 
                Action = $"User {CurrentUser?.FullName ?? "System"} deleted user ID #{userId} ({user.FullName})." 
            });
            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    // Step 4: Timesheet Verification & Exception Queries
    public async Task<List<PunchRecord>> GetAllPunchRecords()
    {
        return await _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .OrderByDescending(p => p.PunchTime)
            .ToListAsync();
    }

    public async Task SavePunchRecord(PunchRecord record)
    {
        _db.PunchRecords.Update(record);
        _db.AuditLogs.Add(new AuditLog 
        { 
            Action = $"User {CurrentUser?.FullName ?? "System"} updated/approved PunchRecord ID #{record.Id}." 
        });
        await _db.SaveChangesAsync();
    }

    // Supervisor & Reporting Queries
    public async Task<List<ActiveEmployee>> GetActiveEmployees()
    {
        var punches = await _db.PunchRecords.Include(p => p.User).Include(p => p.Site)
            .Where(p => p.PunchTime.Date == DateTime.Today).ToListAsync();

        return punches.GroupBy(p => p.UserId)
            .Select(g => g.OrderByDescending(p => p.PunchTime).First())
            .Where(p => p.IsPunchIn)
            .Select(p => new ActiveEmployee {
                Name = p.User?.FullName ?? "Unknown",
                SiteName = p.Site?.Name ?? "N/A",
                ClockedInAt = p.PunchTime.ToString("hh:mm tt")
            }).ToList();
    }

    public async Task<List<PunchRecord>> GetAllRawPunches()
    {
        return await _db.PunchRecords.Include(p => p.User).Include(p => p.Site)
            .OrderByDescending(p => p.PunchTime).ToListAsync();
    }

    public async Task<List<PunchRecord>> GetUserTodayRecords()
    {
        if (CurrentUser == null) return new();
        return await _db.PunchRecords.Include(p => p.Site)
            .Where(p => p.UserId == CurrentUser.Id && p.PunchTime.Date == DateTime.Today)
            .OrderByDescending(p => p.PunchTime).ToListAsync();
    }

    // Punch Recording with Phase 2 GPS Logic
    public async Task<bool> RecordPunch(int siteId, bool isPunchIn, double? lat = null, double? lng = null)
    {
        if (CurrentUser == null) return false;

        var site = await _db.Sites.FindAsync(siteId);
        bool isFlagged = false;
        string? flagReason = null;

        if (site != null && lat.HasValue && lng.HasValue && site.Latitude != 0 && site.Longitude != 0)
        {
            double distanceMeters = CalculateDistanceMeters(lat.Value, lng.Value, site.Latitude, site.Longitude);
            if (distanceMeters > site.RadiusMeters)
            {
                isFlagged = true;
                flagReason = $"Punch occurred {Math.Round(distanceMeters)}m away from site boundary (Max {site.RadiusMeters}m).";
            }
        }

        var record = new PunchRecord 
        { 
            UserId = CurrentUser.Id, 
            SiteId = siteId, 
            PunchTime = DateTime.Now, 
            IsPunchIn = isPunchIn,
            Latitude = lat,
            Longitude = lng,
            IsFlagged = isFlagged,
            FlagReason = flagReason
        };

        _db.PunchRecords.Add(record);
        _db.AuditLogs.Add(new AuditLog { Action = $"User {CurrentUser.FullName} punched {(isPunchIn ? "IN" : "OUT")} at Site #{siteId}. Flagged: {isFlagged}" });
        await _db.SaveChangesAsync();

        OnNotify?.Invoke();
        return true;
    }

    // Break Tracking
    public async Task<bool> StartBreak(int punchRecordId)
    {
        if (CurrentUser == null) return false;

        var breakLog = new BreakRecord
        {
            UserId = CurrentUser.Id,
            PunchRecordId = punchRecordId,
            BreakStart = DateTime.Now
        };

        _db.BreakRecords.Add(breakLog);
        _db.AuditLogs.Add(new AuditLog { Action = $"User {CurrentUser.FullName} started break." });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
        return true;
    }

    public async Task<bool> EndBreak(int punchRecordId, double maxBreakMinutes = 30.0)
    {
        if (CurrentUser == null) return false;

        var breakLog = await _db.BreakRecords
            .Where(b => b.UserId == CurrentUser.Id && b.PunchRecordId == punchRecordId && b.BreakEnd == null)
            .OrderByDescending(b => b.BreakStart)
            .FirstOrDefaultAsync();

        if (breakLog == null) return false;

        breakLog.BreakEnd = DateTime.Now;
        double duration = (breakLog.BreakEnd.Value - breakLog.BreakStart).TotalMinutes;
        if (duration > maxBreakMinutes)
        {
            breakLog.IsExceeded = true;
        }

        _db.AuditLogs.Add(new AuditLog { Action = $"User {CurrentUser.FullName} ended break. Duration: {Math.Round(duration)} mins." });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
        return true;
    }

    public async Task<BreakRecord?> GetActiveBreak()
    {
        if (CurrentUser == null) return null;
        return await _db.BreakRecords
            .FirstOrDefaultAsync(b => b.UserId == CurrentUser.Id && b.BreakEnd == null);
    }

    // Corrections & Adjustments
    public async Task<List<CorrectionRequest>> GetPendingCorrections() =>
        await _db.CorrectionRequests.Include(r => r.OriginalRecord).ThenInclude(p => p!.User)
            .Where(r => r.Status == RequestStatus.Pending).ToListAsync();

    public async Task UpdateCorrectionStatus(CorrectionRequest req, RequestStatus status)
    {
        var dbReq = await _db.CorrectionRequests.FindAsync(req.Id);
        if (dbReq != null) 
        { 
            dbReq.Status = status; 
            await _db.SaveChangesAsync(); 
        }
    }

    public async Task SubmitCorrection(int punchId, DateTime requestedTime, string reason)
    {
        var request = new CorrectionRequest {
            PunchRecordId = punchId,
            RequestedTime = requestedTime,
            Reason = reason,
            Status = RequestStatus.Pending,
            RequestDate = DateTime.Now
        };
        _db.CorrectionRequests.Add(request);
        await _db.SaveChangesAsync();
    }

    // Leave Requests & Approvals
    public async Task SubmitLeaveRequest(LeaveRequest request)
    {
        if (CurrentUser == null) return;
        request.UserId = CurrentUser.Id;
        request.Status = RequestStatus.Pending;
        request.RequestDate = DateTime.Now;

        _db.LeaveRequests.Add(request);
        _db.AuditLogs.Add(new AuditLog 
        { 
            Action = $"User {CurrentUser.FullName} requested {request.LeaveType} leave from {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}." 
        });

        // Notify Admins & Supervisors
        var managers = await _db.Users
            .Where(u => u.Role >= UserRole.Supervisor && u.IsActive)
            .ToListAsync();

        foreach (var mgr in managers)
        {
            _db.Notifications.Add(new NotificationItem
            {
                UserId = mgr.Id,
                Title = "New Leave Request",
                Message = $"{CurrentUser.FullName} requested {request.LeaveType} leave.",
                Url = "/leave-management"
            });
        }

        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    public async Task<List<LeaveRequest>> GetUserLeaveRequests()
    {
        if (CurrentUser == null) return new();
        return await _db.LeaveRequests
            .Where(l => l.UserId == CurrentUser.Id)
            .OrderByDescending(l => l.RequestDate)
            .ToListAsync();
    }

    public async Task<List<LeaveRequest>> GetPendingLeaveRequests()
    {
        return await _db.LeaveRequests
            .Include(l => l.User)
            .Where(l => l.Status == RequestStatus.Pending)
            .OrderByDescending(l => l.RequestDate)
            .ToListAsync();
    }

    public async Task UpdateLeaveStatus(int requestId, RequestStatus status, string? notes = null)
    {
        var req = await _db.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == requestId);
        if (req != null)
        {
            req.Status = status;
            req.ReviewerNotes = notes;
            _db.AuditLogs.Add(new AuditLog 
            { 
                Action = $"User {CurrentUser?.FullName ?? "System"} updated Leave Request #{requestId} to {status}." 
            });

            // Notify User
            _db.Notifications.Add(new NotificationItem
            {
                UserId = req.UserId,
                Title = $"Leave Request {status}",
                Message = $"Your {req.LeaveType} leave request has been {status.ToString().ToLower()}.",
                Url = "/leave-management"
            });

            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    // Notifications Management
    public async Task<List<NotificationItem>> GetUserNotifications()
    {
        if (CurrentUser == null) return new();
        return await _db.Notifications
            .Where(n => n.UserId == CurrentUser.Id && !n.IsRead)
            .OrderByDescending(n => n.CreatedAt)
            .ToListAsync();
    }

    public async Task MarkNotificationAsRead(int id)
    {
        var notif = await _db.Notifications.FindAsync(id);
        if (notif != null)
        {
            notif.IsRead = true;
            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    // Reports & CSV Export
    public async Task<List<TimesheetEntry>> GetTimesheetReport()
    {
        return await _db.PunchRecords.Include(p => p.User).OrderByDescending(p => p.PunchTime)
            .Select(p => new TimesheetEntry { 
                EmployeeName = p.User!.FullName, 
                Date = p.PunchTime.ToString("yyyy-MM-dd HH:mm"), 
                IsPunchIn = p.IsPunchIn 
            }).ToListAsync();
    }

    public async Task<List<LaborCostReportItem>> GetLaborCostReport(DateTime startDate, DateTime endDate, int? siteId = null)
    {
        var query = _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .Where(p => p.PunchTime.Date >= startDate.Date && p.PunchTime.Date <= endDate.Date);

        if (siteId.HasValue && siteId.Value > 0)
        {
            query = query.Where(p => p.SiteId == siteId.Value);
        }

        var punches = await query.OrderBy(p => p.PunchTime).ToListAsync();
        var report = new List<LaborCostReportItem>();

        var groupedByUser = punches.GroupBy(p => p.UserId);

        foreach (var userGroup in groupedByUser)
        {
            var user = userGroup.First().User;
            if (user == null) continue;

            double totalHours = 0;
            PunchRecord? lastIn = null;

            foreach (var punch in userGroup.OrderBy(p => p.PunchTime))
            {
                if (punch.IsPunchIn)
                {
                    lastIn = punch;
                }
                else if (lastIn != null)
                {
                    totalHours += (punch.PunchTime - lastIn.PunchTime).TotalHours;
                    lastIn = null;
                }
            }

            if (totalHours > 0)
            {
                report.Add(new LaborCostReportItem
                {
                    EmployeeName = user.FullName,
                    SiteName = userGroup.First().Site?.Name ?? "N/A",
                    TotalHours = Math.Round(totalHours, 2),
                    HourlyRate = (CurrentUser?.Role == UserRole.Admin) ? user.HourlyPayRate : 0m
                });
            }
        }

        return report;
    }

    public async Task<List<ExceptionEntry>> GetExceptionReport()
    {
        var punches = await _db.PunchRecords.Include(p => p.User).Include(p => p.Site)
            .Where(p => p.PunchTime.Date == DateTime.Today).ToListAsync();

        return punches.GroupBy(p => p.UserId).Where(g => g.Count() % 2 != 0)
            .Select(g => new ExceptionEntry { 
                EmployeeName = g.First().User?.FullName ?? "Unknown", 
                SiteName = g.First().Site?.Name ?? "N/A", 
                Issue = "Missing Punch Out" 
            }).ToList();
    }

    public async Task<string> GetTimesheetCsv()
    {
        var records = await _db.PunchRecords.Include(p => p.User).ToListAsync();
        var csv = new StringBuilder().AppendLine("Employee,Date,Type");
        foreach (var r in records) csv.AppendLine($"{r.User?.FullName},{r.PunchTime},{(r.IsPunchIn ? "IN" : "OUT")}");
        return csv.ToString();
    }

    // Audit Log Queries
    public async Task<List<AuditLog>> GetAuditLogs()
    {
        return await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3; // Earth radius in meters
        var phi1 = lat1 * Math.PI / 180;
        var phi2 = lat2 * Math.PI / 180;
        var deltaPhi = (lat2 - lat1) * Math.PI / 180;
        var deltaLambda = (lon2 - lon1) * Math.PI / 180;

        var a = Math.Sin(deltaPhi / 2) * Math.Sin(deltaPhi / 2) +
                Math.Cos(phi1) * Math.Cos(phi2) *
                Math.Sin(deltaLambda / 2) * Math.Sin(deltaLambda / 2);

        var c = 2 * Math.Atan2(Math.Sqrt(a), Math.Sqrt(1 - a));
        return r * c;
    }
}