namespace AMS.Models;

public enum UserRole 
{ 
    Cleaner,
    Supervisor, 
    Manager, 
    Admin, 
    SystemAdmin 
}

public enum EmploymentType
{
    FullTime,
    PartTime,
    Casual,
    Contractor
}

public enum RequestStatus { Pending, Approved, Rejected }

public enum LeaveType
{
    Annual,
    Sick,
    Unpaid
}

// Helper Classes for Reports/Dashboard
public class TimesheetEntry 
{ 
    public string EmployeeName { get; set; } = string.Empty; 
    public string Date { get; set; } = string.Empty; 
    public bool IsPunchIn { get; set; } 
}

public class ExceptionEntry 
{ 
    public string EmployeeName { get; set; } = string.Empty; 
    public string SiteName { get; set; } = string.Empty; 
    public string Issue { get; set; } = string.Empty; 
}

public class ActiveEmployee 
{
    public string Name { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public string ClockedInAt { get; set; } = string.Empty;
}

public class LaborCostReportItem
{
    public string EmployeeName { get; set; } = string.Empty;
    public string SiteName { get; set; } = string.Empty;
    public double TotalHours { get; set; }
    public decimal HourlyRate { get; set; }
    public decimal TotalCost => (decimal)TotalHours * HourlyRate;
}

// Database Entities
public class User
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string PhoneNumber { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public UserRole Role { get; set; }
    public bool IsActive { get; set; } = true;

    // Phase 2 Additions
    public string EmergencyContact { get; set; } = string.Empty;
    public EmploymentType EmploymentType { get; set; } = EmploymentType.Casual;
    public DateTime StartDate { get; set; } = DateTime.Today;
    
    // Restricted Field
    public decimal HourlyPayRate { get; set; }
}

public class Site
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    
    // GPS Geofencing Fields
    public double Latitude { get; set; }
    public double Longitude { get; set; }
    public double RadiusMeters { get; set; } = 200.0;
}

public class PunchRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int SiteId { get; set; }
    public Site? Site { get; set; }
    public DateTime PunchTime { get; set; }
    public bool IsPunchIn { get; set; }

    // GPS Punch Capture & Flagging
    public double? Latitude { get; set; }
    public double? Longitude { get; set; }
    public bool IsFlagged { get; set; }
    public string? FlagReason { get; set; }
}

public class BreakRecord
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public int PunchRecordId { get; set; }
    public PunchRecord? PunchRecord { get; set; }
    
    public DateTime BreakStart { get; set; }
    public DateTime? BreakEnd { get; set; }
    public bool IsExceeded { get; set; }
}

public class CorrectionRequest
{
    public int Id { get; set; }
    public int? PunchRecordId { get; set; }
    public PunchRecord? OriginalRecord { get; set; }
    public DateTime RequestedTime { get; set; }
    public string Reason { get; set; } = string.Empty;
    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime RequestDate { get; set; } = DateTime.Now;
}

public class LeaveRequest
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }

    public LeaveType LeaveType { get; set; }
    public DateTime StartDate { get; set; }
    public DateTime EndDate { get; set; }
    public string Reason { get; set; } = string.Empty;

    public RequestStatus Status { get; set; } = RequestStatus.Pending;
    public DateTime RequestDate { get; set; } = DateTime.Now;
    public string? ReviewerNotes { get; set; }
}

public class NotificationItem
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User? User { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.Now;
    public string? Url { get; set; }
}

public class AuditLog 
{
    public int Id { get; set; }
    public string Action { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; } = DateTime.Now;
}