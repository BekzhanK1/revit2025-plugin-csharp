-- Task 01: Revit fields on material_tab
-- Feature: revit-materials-sync
-- Idempotent: safe to re-run (ADD COLUMN IF NOT EXISTS, constraints via DO blocks)
-- Deploy copy: sql/revit-materials-sync/01_material_tab_revit_fields.sql

ALTER TABLE public.material_tab
  ADD COLUMN IF NOT EXISTS revit_file_type   varchar(20) NOT NULL DEFAULT 'none',
  ADD COLUMN IF NOT EXISTS revit_file_url    varchar(2000),
  ADD COLUMN IF NOT EXISTS revit_file_hash   varchar(64),
  ADD COLUMN IF NOT EXISTS revit_asset_name  varchar(200);

-- Allowed values for revit_file_type
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'material_tab_revit_file_type_check'
      AND conrelid = 'public.material_tab'::regclass
  ) THEN
    ALTER TABLE public.material_tab
      ADD CONSTRAINT material_tab_revit_file_type_check
      CHECK (revit_file_type IN ('rfa', 'surface', 'none'));
  END IF;
END $$;

-- When type is 'none', Revit-related fields must stay NULL
DO $$
BEGIN
  IF NOT EXISTS (
    SELECT 1
    FROM pg_constraint
    WHERE conname = 'material_tab_revit_none_fields_null_check'
      AND conrelid = 'public.material_tab'::regclass
  ) THEN
    ALTER TABLE public.material_tab
      ADD CONSTRAINT material_tab_revit_none_fields_null_check
      CHECK (
        revit_file_type <> 'none'
        OR (
          revit_file_url IS NULL
          AND revit_file_hash IS NULL
          AND revit_asset_name IS NULL
        )
      );
  END IF;
END $$;
