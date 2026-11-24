using AMS.Data;
using AMS.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using System.Text;

namespace AMS.Services;

public class AttendanceService
{
    private readonly AmsDbContext _db;
    private readonly PasswordHasher<User> _passwordHasher = new();
    public User? CurrentUser { get; private set; }
    public event Action? OnNotify;

    public AttendanceService(AmsDbContext db) => _db = db;

    private bool IsAdminOrSystemAdmin() =>
        CurrentUser != null && (CurrentUser.Role == UserRole.Admin || CurrentUser.Role == UserRole.SystemAdmin);

    private bool IsSupervisorOrAbove() =>
        CurrentUser != null && CurrentUser.Role >= UserRole.Supervisor;

    private bool IsManagerOrAbove() =>
        CurrentUser != null && CurrentUser.Role >= UserRole.Manager;

    // Authentication & Session Restoration
    public async Task<User?> Login(string email, string password)
    {
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email == email && u.IsActive);
        if (user == null) return null;

        // Verify PBKDF2 hash (also supports seamless migration of legacy plain-text passwords on first login)
        bool isValid = false;
        if (user.Password.StartsWith("AQAAAA"))
        {
            var result = _passwordHasher.VerifyHashedPassword(user, user.Password, password);
            isValid = result != PasswordVerificationResult.Failed;
        }
        else if (user.Password == password)
        {
            isValid = true;
            user.Password = _passwordHasher.HashPassword(user, password);
            await _db.SaveChangesAsync();
        }

        if (isValid)
        {
            CurrentUser = user;
            OnNotify?.Invoke();
            return user;
        }
        return null;
    }

    public async Task RestoreSessionAsync(int userId)
    {
        if (CurrentUser != null && CurrentUser.Id == userId) return;
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Id == userId && u.IsActive);
        if (user != null)
        {
            CurrentUser = user;
            OnNotify?.Invoke();
        }
    }

    public void Logout()
    {
        CurrentUser = null;
        OnNotify?.Invoke();
    }

    public async Task<List<User>> GetAllUsers()
    {
        if (!IsSupervisorOrAbove()) return new();

        var users = await _db.Users.ToListAsync();
        // Strip pay rates below UI level if caller is not Admin/SystemAdmin
        if (!IsAdminOrSystemAdmin())
        {
            foreach (var u in users) u.HourlyPayRate = 0m;
        }
        return users;
    }

    public async Task<List<Site>> GetSites()
    {
        if (CurrentUser == null) return new();
        return await _db.Sites.ToListAsync();
    }

    public async Task SaveSite(Site site)
    {
        if (!IsManagerOrAbove()) return;

        if (site.Id == 0) _db.Sites.Add(site);
        else _db.Sites.Update(site);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"User {CurrentUser!.FullName} updated Site '{site.Name}' (Radius: {site.RadiusMeters}m, MaxBreak: {site.MaxBreakMinutes}m)."
        });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    public async Task SaveUser(User user)
    {
        // Only Admins/SystemAdmins can create/edit users or modify pay rates/roles
        if (!IsAdminOrSystemAdmin())
            return;

        if (!string.IsNullOrWhiteSpace(user.Password) && !user.Password.StartsWith("AQAAAA"))
        {
            user.Password = _passwordHasher.HashPassword(user, user.Password);
        }

        if (user.Id == 0) _db.Users.Add(user);
        else _db.Users.Update(user);

        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"Admin {CurrentUser!.FullName} saved profile for {user.FullName} (Role: {user.Role})."
        });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    public async Task UpdateEmployeeLeaveBalance(int userId, double newBalance)
    {
        if (!IsAdminOrSystemAdmin())
            return;

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            user.LeaveBalanceDays = newBalance;
            _db.AuditLogs.Add(new AuditLog
            {
                Action = $"Admin {CurrentUser!.FullName} adjusted leave balance for {user.FullName} to {newBalance} days."
            });
            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    public async Task DeleteUser(int userId)
    {
        if (!IsAdminOrSystemAdmin())
            return;

        var user = await _db.Users.FindAsync(userId);
        if (user != null)
        {
            _db.Users.Remove(user);
            _db.AuditLogs.Add(new AuditLog
            {
                Action = $"User {CurrentUser!.FullName} deleted user ID #{userId} ({user.FullName})."
            });
            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    // Step 4: Timesheet Verification & Exception Queries
    public async Task<List<PunchRecord>> GetAllPunchRecords()
    {
        if (!IsSupervisorOrAbove()) return new();

        return await _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .OrderByDescending(p => p.PunchTime)
            .ToListAsync();
    }

    public async Task<List<PunchRecord>> GetAllRawPunches()
    {
        if (!IsSupervisorOrAbove()) return new();

        return await _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .OrderByDescending(p => p.PunchTime)
            .ToListAsync();
    }

    public async Task SavePunchRecord(PunchRecord record)
    {
        if (!IsSupervisorOrAbove()) return;

        _db.PunchRecords.Update(record);
        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"User {CurrentUser!.FullName} updated/approved PunchRecord ID #{record.Id}."
        });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    // Real-Time Dashboard: All 5 Required Statuses (Working, On Break, Late, Not Yet Clocked In, Finished)
    public async Task<List<ActiveEmployee>> GetActiveEmployees()
    {
        if (!IsSupervisorOrAbove()) return new();

        var activeUsers = await _db.Users.Where(u => u.IsActive).ToListAsync();
        var sites = await _db.Sites.ToDictionaryAsync(s => s.Id, s => s.Name);
        var todayPunches = await _db.PunchRecords
            .Include(p => p.Site)
            .Where(p => p.PunchTime.Date == DateTime.Today)
            .ToListAsync();
        var activeBreaks = await _db.BreakRecords
            .Where(b => b.BreakStart.Date == DateTime.Today && b.BreakEnd == null)
            .Select(b => b.UserId)
            .ToListAsync();
        var approvedLeavesToday = await _db.LeaveRequests
            .Where(l => l.Status == RequestStatus.Approved && l.StartDate.Date <= DateTime.Today && l.EndDate.Date >= DateTime.Today)
            .Select(l => l.UserId)
            .ToListAsync();

        var result = new List<ActiveEmployee>();
        var nowTimeOfDay = DateTime.Now.TimeOfDay;

        foreach (var user in activeUsers)
        {
            if (approvedLeavesToday.Contains(user.Id)) continue;

            var userPunches = todayPunches
                .Where(p => p.UserId == user.Id)
                .OrderBy(p => p.PunchTime)
                .ToList();

            var firstPunchIn = userPunches.FirstOrDefault(p => p.IsPunchIn);
            var lastPunch = userPunches.LastOrDefault();

            string status;
            string siteName = lastPunch?.Site?.Name
                ?? (user.AssignedSiteId.HasValue && sites.ContainsKey(user.AssignedSiteId.Value) ? sites[user.AssignedSiteId.Value] : "Unassigned");
            string clockedInAt = firstPunchIn?.PunchTime.ToString("hh:mm tt") ?? string.Empty;

            if (lastPunch == null)
            {
                status = nowTimeOfDay > user.ExpectedStartTime ? "Late" : "Not Yet Clocked In";
            }
            else if (!lastPunch.IsPunchIn)
            {
                status = "Finished";
            }
            else if (activeBreaks.Contains(user.Id))
            {
                status = "On Break";
            }
            else if (firstPunchIn != null && firstPunchIn.PunchTime.TimeOfDay > user.ExpectedStartTime.Add(TimeSpan.FromMinutes(5)))
            {
                status = "Late";
            }
            else
            {
                status = "Working";
            }

            result.Add(new ActiveEmployee
            {
                Name = user.FullName,
                SiteName = siteName,
                ClockedInAt = clockedInAt,
                Status = status
            });
        }

        return result;
    }

    public async Task<List<PunchRecord>> GetUserTodayRecords()
    {
        if (CurrentUser == null) return new();
        return await _db.PunchRecords.Include(p => p.Site)
            .Where(p => p.UserId == CurrentUser.Id && p.PunchTime.Date == DateTime.Today)
            .OrderByDescending(p => p.PunchTime).ToListAsync();
    }

    // Punch Recording with GPS Flagging (including denied/blocked GPS)
    public async Task<bool> RecordPunch(int siteId, bool isPunchIn, double? lat = null, double? lng = null)
    {
        if (CurrentUser == null) return false;

        var site = await _db.Sites.FindAsync(siteId);
        bool isFlagged = false;
        string? flagReason = null;

        if (!lat.HasValue || !lng.HasValue)
        {
            isFlagged = true;
            flagReason = "Location not captured (GPS blocked or unavailable)";
        }
        else if (site != null && site.Latitude != 0 && site.Longitude != 0)
        {
            double distanceMeters = CalculateDistanceMeters(lat.Value, lng.Value, site.Latitude, site.Longitude);
            if (distanceMeters > site.RadiusMeters)
            {
                isFlagged = true;
                flagReason = $"Outside site radius ({Math.Round(distanceMeters)}m vs {site.RadiusMeters}m max)";
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

    // Break Tracking with Service-Level State Enforcement & Site Policy Limits
    public async Task<bool> StartBreak(int punchRecordId)
    {
        if (CurrentUser == null) return false;

        var latestPunch = await _db.PunchRecords
            .Where(p => p.UserId == CurrentUser.Id)
            .OrderByDescending(p => p.PunchTime)
            .FirstOrDefaultAsync();

        if (latestPunch == null || !latestPunch.IsPunchIn)
            return false;

        var existingBreak = await _db.BreakRecords
            .FirstOrDefaultAsync(b => b.UserId == CurrentUser.Id && b.BreakEnd == null);

        if (existingBreak != null)
            return false;

        var breakLog = new BreakRecord
        {
            UserId = CurrentUser.Id,
            PunchRecordId = latestPunch.Id,
            BreakStart = DateTime.Now
        };

        _db.BreakRecords.Add(breakLog);
        _db.AuditLogs.Add(new AuditLog { Action = $"User {CurrentUser.FullName} started break." });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
        return true;
    }

    public async Task<bool> EndBreak(int punchRecordId)
    {
        if (CurrentUser == null) return false;

        var breakLog = await _db.BreakRecords
            .Include(b => b.PunchRecord)
            .ThenInclude(p => p!.Site)
            .Where(b => b.UserId == CurrentUser.Id && b.BreakEnd == null)
            .OrderByDescending(b => b.BreakStart)
            .FirstOrDefaultAsync();

        if (breakLog == null) return false;

        double maxBreakMinutes = breakLog.PunchRecord?.Site?.MaxBreakMinutes ?? 30.0;

        breakLog.BreakEnd = DateTime.Now;
        double duration = (breakLog.BreakEnd.Value - breakLog.BreakStart).TotalMinutes;
        if (duration > maxBreakMinutes)
        {
            breakLog.IsExceeded = true;
            if (breakLog.PunchRecord != null)
            {
                breakLog.PunchRecord.IsFlagged = true;
                var breakFlag = $"Break exceeded ({Math.Round(duration)}m / {maxBreakMinutes}m max)";
                breakLog.PunchRecord.FlagReason = string.IsNullOrEmpty(breakLog.PunchRecord.FlagReason)
                    ? breakFlag
                    : $"{breakLog.PunchRecord.FlagReason}; {breakFlag}";
            }
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

    // Corrections & Adjustments (with In-App Notifications)
    public async Task<List<CorrectionRequest>> GetPendingCorrections()
    {
        if (!IsSupervisorOrAbove()) return new();

        return await _db.CorrectionRequests
            .Include(r => r.OriginalRecord)
            .ThenInclude(p => p!.User)
            .Where(r => r.Status == RequestStatus.Pending)
            .ToListAsync();
    }

    public async Task UpdateCorrectionStatus(CorrectionRequest req, RequestStatus status)
    {
        if (!IsSupervisorOrAbove()) return;

        var dbReq = await _db.CorrectionRequests
            .Include(r => r.OriginalRecord)
            .FirstOrDefaultAsync(r => r.Id == req.Id);

        if (dbReq != null)
        {
            dbReq.Status = status;

            if (dbReq.OriginalRecord != null)
            {
                _db.Notifications.Add(new NotificationItem
                {
                    UserId = dbReq.OriginalRecord.UserId,
                    Title = $"Timesheet Correction {status}",
                    Message = $"Your punch correction request for {dbReq.RequestedTime:MMM dd, hh:mm tt} was {status.ToString().ToLower()}.",
                    Url = "/punchclock"
                });
            }

            _db.AuditLogs.Add(new AuditLog
            {
                Action = $"User {CurrentUser!.FullName} updated Correction Request #{req.Id} to {status}."
            });

            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    public async Task SubmitCorrection(int punchId, DateTime requestedTime, string reason)
    {
        if (CurrentUser == null) return;

        var request = new CorrectionRequest
        {
            PunchRecordId = punchId,
            RequestedTime = requestedTime,
            Reason = reason,
            Status = RequestStatus.Pending,
            RequestDate = DateTime.Now
        };
        _db.CorrectionRequests.Add(request);
        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"User {CurrentUser.FullName} submitted correction request for PunchRecord #{punchId}."
        });
        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
    }

    // Leave Requests & Approvals
    public async Task SubmitLeaveRequest(LeaveRequest request)
    {
        if (CurrentUser == null) return;
        if (request.EndDate.Date < request.StartDate.Date)
            throw new ArgumentException("End date cannot be earlier than start date.");

        request.UserId = CurrentUser.Id;
        request.Status = RequestStatus.Pending;
        request.RequestDate = DateTime.Now;

        _db.LeaveRequests.Add(request);
        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"User {CurrentUser.FullName} requested {request.LeaveType} leave from {request.StartDate:yyyy-MM-dd} to {request.EndDate:yyyy-MM-dd}."
        });

        var managers = await _db.Users
            .Where(u => u.Role >= UserRole.Supervisor && u.IsActive && u.Id != CurrentUser.Id)
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
        if (!IsSupervisorOrAbove()) return new();

        return await _db.LeaveRequests
            .Include(l => l.User)
            .Where(l => l.Status == RequestStatus.Pending)
            .OrderByDescending(l => l.RequestDate)
            .ToListAsync();
    }

    public async Task<List<LeaveRequest>> GetApprovedLeavesForMonth(int year, int month)
    {
        if (CurrentUser == null) return new();

        var monthStart = new DateTime(year, month, 1);
        var monthEnd = monthStart.AddMonths(1).AddDays(-1);
        return await _db.LeaveRequests
            .Include(l => l.User)
            .Where(l => l.Status == RequestStatus.Approved && l.StartDate.Date <= monthEnd && l.EndDate.Date >= monthStart)
            .ToListAsync();
    }

    public async Task UpdateLeaveStatus(int requestId, RequestStatus status, string? notes = null)
    {
        if (!IsSupervisorOrAbove()) return;

        var req = await _db.LeaveRequests.Include(l => l.User).FirstOrDefaultAsync(l => l.Id == requestId);
        if (req == null) return;

        if (req.UserId == CurrentUser!.Id)
            return;

        req.Status = status;
        req.ReviewerNotes = notes;

        if (status == RequestStatus.Approved && req.User != null && req.LeaveType != LeaveType.Unpaid)
        {
            double days = (req.EndDate.Date - req.StartDate.Date).TotalDays + 1;
            req.User.LeaveBalanceDays = Math.Max(0, req.User.LeaveBalanceDays - days);
        }

        _db.AuditLogs.Add(new AuditLog
        {
            Action = $"User {CurrentUser.FullName} updated Leave Request #{requestId} to {status}."
        });

        _db.Notifications.Add(new NotificationItem
        {
            UserId = req.UserId,
            Title = $"Leave Request {status}",
            Message = $"Your {req.LeaveType} leave request has been {status.ToString().ToLower()}." +
                      (!string.IsNullOrWhiteSpace(notes) ? $" Note: {notes}" : ""),
            Url = "/leave-management"
        });

        await _db.SaveChangesAsync();
        OnNotify?.Invoke();
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
        if (CurrentUser == null) return;

        var notif = await _db.Notifications.FirstOrDefaultAsync(n => n.Id == id && n.UserId == CurrentUser.Id);
        if (notif != null)
        {
            notif.IsRead = true;
            await _db.SaveChangesAsync();
            OnNotify?.Invoke();
        }
    }

    // Reports & CSV Export (with Break Deductions, Multi-Site Grouping & Boundary Shift Handling)
    public async Task<List<TimesheetEntry>> GetTimesheetReport()
    {
        if (!IsSupervisorOrAbove()) return new();

        return await _db.PunchRecords.Include(p => p.User).OrderByDescending(p => p.PunchTime)
            .Select(p => new TimesheetEntry
            {
                EmployeeName = p.User!.FullName,
                Date = p.PunchTime.ToString("yyyy-MM-dd HH:mm"),
                IsPunchIn = p.IsPunchIn
            }).ToListAsync();
    }

    public async Task<List<LaborCostReportItem>> GetLaborCostReport(DateTime startDate, DateTime endDate, int? siteId = null)
    {
        if (!IsManagerOrAbove())
            return new();

        var rangeStart = startDate.Date;
        var rangeEnd = endDate.Date.AddDays(1).AddTicks(-1);

        var queryStart = rangeStart.AddDays(-1);
        var queryEnd = rangeEnd.AddDays(1);

        var query = _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .Where(p => p.PunchTime >= queryStart && p.PunchTime <= queryEnd);

        if (siteId.HasValue && siteId.Value > 0)
        {
            query = query.Where(p => p.SiteId == siteId.Value);
        }

        var punches = await query.OrderBy(p => p.PunchTime).ToListAsync();
        var breaks = await _db.BreakRecords
            .Where(b => b.BreakStart <= queryEnd && (b.BreakEnd == null || b.BreakEnd >= queryStart))
            .ToListAsync();

        var report = new List<LaborCostReportItem>();

        var groupedByUserAndSite = punches.GroupBy(p => new { p.UserId, p.SiteId });

        foreach (var group in groupedByUserAndSite)
        {
            var user = group.First().User;
            var siteName = group.First().Site?.Name ?? "N/A";
            if (user == null) continue;

            double netHours = 0;
            PunchRecord? lastIn = null;

            foreach (var punch in group.OrderBy(p => p.PunchTime))
            {
                if (punch.IsPunchIn)
                {
                    lastIn = punch;
                }
                else if (lastIn != null)
                {
                    var effectiveStart = lastIn.PunchTime < rangeStart ? rangeStart : lastIn.PunchTime;
                    var effectiveEnd = punch.PunchTime > rangeEnd ? rangeEnd : punch.PunchTime;

                    if (effectiveEnd > effectiveStart)
                    {
                        double shiftHours = (effectiveEnd - effectiveStart).TotalHours;

                        double breakHours = breaks
                            .Where(b => b.PunchRecordId == lastIn.Id && b.BreakEnd.HasValue)
                            .Sum(b =>
                            {
                                var breakStart = b.BreakStart < effectiveStart ? effectiveStart : b.BreakStart;
                                var breakEnd = b.BreakEnd!.Value > effectiveEnd ? effectiveEnd : b.BreakEnd!.Value;
                                return breakEnd > breakStart ? (breakEnd - breakStart).TotalHours : 0;
                            });

                        netHours += Math.Max(0, shiftHours - breakHours);
                    }
                    lastIn = null;
                }
            }

            if (netHours > 0)
            {
                report.Add(new LaborCostReportItem
                {
                    EmployeeName = user.FullName,
                    SiteName = siteName,
                    TotalHours = Math.Round(netHours, 2),
                    HourlyRate = user.HourlyPayRate
                });
            }
        }

        return report.OrderBy(r => r.SiteName).ThenBy(r => r.EmployeeName).ToList();
    }

    public async Task<List<ExceptionEntry>> GetExceptionReport()
    {
        if (!IsSupervisorOrAbove()) return new();

        var punches = await _db.PunchRecords
            .Include(p => p.User)
            .Include(p => p.Site)
            .Where(p => p.PunchTime.Date == DateTime.Today)
            .ToListAsync();

        return punches
            .GroupBy(p => p.UserId)
            .Where(g => g.Count() % 2 != 0)
            .Select(g => new ExceptionEntry
            {
                EmployeeName = g.First().User?.FullName ?? "Unknown",
                SiteName = g.First().Site?.Name ?? "N/A",
                Issue = "Missing Punch Out"
            })
            .ToList();
    }

    public async Task<string> GetTimesheetCsv()
    {
        if (!IsSupervisorOrAbove()) return string.Empty;

        var records = await _db.PunchRecords.Include(p => p.User).ToListAsync();
        var csv = new StringBuilder().AppendLine("Employee,Date,Type");
        foreach (var r in records) csv.AppendLine($"{r.User?.FullName},{r.PunchTime},{(r.IsPunchIn ? "IN" : "OUT")}");
        return csv.ToString();
    }

    public async Task<List<AuditLog>> GetAuditLogs()
    {
        if (!IsAdminOrSystemAdmin()) return new();

        return await _db.AuditLogs
            .OrderByDescending(a => a.Timestamp)
            .ToListAsync();
    }

    private static double CalculateDistanceMeters(double lat1, double lon1, double lat2, double lon2)
    {
        var r = 6371e3;
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