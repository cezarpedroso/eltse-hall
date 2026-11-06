namespace RaDuty.Domain;

public enum HallRole { ResidentAssistant, HallDirector, AssistantDirectorOfResidenceLife, DeanOfStudents, Admin }
public enum ScheduleStatus { Draft, OpenForSelection, Closed, Published, Archived }
public enum ShiftStatus { Open, Full, Cancelled }
public enum IncidentType { Alcohol, Smoking, SmokeDetectorCovered, Fight, IllegalAppliance, LoudNoise, UnauthorizedRoomMove, Assault, Other }
public enum AssignmentStatus { Pending, Confirmed }
