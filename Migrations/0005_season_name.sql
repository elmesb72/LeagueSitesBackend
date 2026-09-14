-- 0005: Seasons carry their name.
--
-- Until now a season's name was computed as "{Year} {Subseason}". Mid-season
-- tournaments are their own seasons (Subseason = 'Tournament') and need a name
-- of their own ("2027 Canada Day Cup"), so the name becomes a stored column for
-- every season: existing rows keep the name they always displayed, and new rows
-- set it at creation.
ALTER TABLE Season ADD COLUMN Name TEXT NOT NULL DEFAULT '';

UPDATE Season SET Name = CAST(Year AS TEXT) || ' ' || Subseason WHERE Name = '';
