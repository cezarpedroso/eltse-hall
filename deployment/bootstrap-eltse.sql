SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @HallId uniqueidentifier = '10000000-0000-0000-0000-000000000001';

IF NOT EXISTS (SELECT 1 FROM dbo.ResidenceHalls WHERE Id = @HallId)
BEGIN
    INSERT INTO dbo.ResidenceHalls (Id, Name, TimeZone, IsActive)
    VALUES (@HallId, N'Eltse Hall', N'America/Chicago', 1);
END
ELSE
BEGIN
    UPDATE dbo.ResidenceHalls
    SET Name = N'Eltse Hall', TimeZone = N'America/Chicago', IsActive = 1
    WHERE Id = @HallId;
END;

;WITH SuiteNumbers AS
(
    SELECT SuiteNumber
    FROM (VALUES
        ('01'), ('02'), ('03'), ('04'), ('05'),
        ('06'), ('07'), ('08'), ('09'), ('10'),
        ('11'), ('12'), ('13'), ('14'), ('15'),
        ('16'), ('17'), ('18'), ('19'), ('20'),
        ('21'), ('22'), ('23'), ('24'), ('25')
    ) AS Suites(SuiteNumber)
),
RoomLetters AS
(
    SELECT RoomLetter
    FROM (VALUES ('A'), ('B'), ('C'), ('D')) AS Letters(RoomLetter)
)
INSERT INTO dbo.DormRooms (Id, ResidenceHallId, SuiteNumber, RoomLetter)
SELECT NEWID(), @HallId, Suites.SuiteNumber, Letters.RoomLetter
FROM SuiteNumbers AS Suites
CROSS JOIN RoomLetters AS Letters
WHERE NOT EXISTS
(
    SELECT 1
    FROM dbo.DormRooms AS Existing
    WHERE Existing.ResidenceHallId = @HallId
      AND Existing.SuiteNumber = Suites.SuiteNumber
      AND Existing.RoomLetter = Letters.RoomLetter
);

COMMIT TRANSACTION;

SELECT Hall.Name, Hall.TimeZone, COUNT(Room.Id) AS RoomCount
FROM dbo.ResidenceHalls AS Hall
LEFT JOIN dbo.DormRooms AS Room ON Room.ResidenceHallId = Hall.Id
WHERE Hall.Id = @HallId
GROUP BY Hall.Name, Hall.TimeZone;
