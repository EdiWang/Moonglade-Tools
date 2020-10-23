DECLARE @SourceCatId UNIQUEIDENTIFIER
DECLARE @TargetCatId UNIQUEIDENTIFIER

SET @SourceCatId = '3FEB11A2-6E36-4DCE-8C02-614BEF7ACC62'
SET @TargetCatId = 'D58043FF-B3CB-43DA-9067-522D76D21BE3'

DECLARE @Temp TABLE (PostId UNIQUEIDENTIFIER)

INSERT INTO @Temp
  (
    PostId
  )(
       SELECT pc.PostId
       FROM   PostCategory AS pc
       WHERE  pc.CategoryId IN (@SourceCatId, @TargetCatId)
       GROUP BY
              pc.PostId
       HAVING COUNT(*) >= 2
   )

--SELECT p.Title,
--       c.DisplayName
--FROM   PostCategory         AS pc
--       INNER JOIN Post      AS p
--            ON  p.Id = pc.PostId
--       INNER JOIN Category  AS c
--            ON  c.Id = pc.CategoryId
--WHERE  pc.PostId IN (SELECT t.PostId
--                     FROM   @Temp t)

-- Step 1. Delete records that will fuck up the primary key
DELETE 
FROM   PostCategory
WHERE  CategoryId = @SourceCatId
       AND PostId IN (SELECT t.PostId
                      FROM   @Temp t)

-- Step 2. Update old key to new key
UPDATE PostCategory
SET    CategoryId     = @TargetCatId
WHERE  CategoryId     = @SourceCatId