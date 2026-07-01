-- Revit materials по remont_id: дедуп по material_id, только revit_file_type <> 'none'.
-- Зависимости: utils.get_client_request_id_by_remont, task-01 (revit_* колонки material_tab).
-- Work copy: agent-memory/revit-materials-sync/task/task-02-sql-function/work/02_read_revit_material_by_remont.sql

CREATE OR REPLACE FUNCTION public.read_revit_material_by_remont(
  cur refcursor,
  remont_id_ integer
)
RETURNS refcursor
LANGUAGE plpgsql
AS $function$
DECLARE
  client_request_id_ integer;
  rec record;
  items_ jsonb := '[]'::jsonb;
BEGIN
  IF remont_id_ IS NULL THEN
    RAISE EXCEPTION '{Не указан ремонт}';
  END IF;

  client_request_id_ := utils.get_client_request_id_by_remont(remont_id_);

  IF client_request_id_ IS NULL THEN
    OPEN cur FOR
    SELECT
      remont_id_ AS remont_id,
      NULL::integer AS client_request_id,
      '[]'::jsonb AS data;
    RETURN cur;
  END IF;

  FOR rec IN
    SELECT DISTINCT ON (m.material_id)
      m.material_id,
      m.material_name,
      m.material_type_id,
      mt.material_type_code,
      m.revit_file_type,
      m.revit_file_url,
      m.revit_file_hash,
      m.revit_asset_name
    FROM client_material_tab cm
    JOIN material_tab m ON m.material_id = cm.material_id
    JOIN material_type_tab mt ON mt.material_type_id = m.material_type_id
    WHERE cm.client_request_id = client_request_id_
      AND m.revit_file_type <> 'none'
    ORDER BY m.material_id
  LOOP
    items_ := items_ || jsonb_build_array(to_jsonb(rec));
  END LOOP;

  OPEN cur FOR
  SELECT
    remont_id_ AS remont_id,
    client_request_id_ AS client_request_id,
    items_ AS data;

  RETURN cur;
END;
$function$;
