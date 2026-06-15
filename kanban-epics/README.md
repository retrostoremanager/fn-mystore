# Epic Kanban Files

This directory contains individual kanban files for each epic. Each file includes:
- The original epic task (as a parent/reference task)
- All broken-down subtasks (1-2 day units)

## File Naming Convention

Files are named: `kanban-epic-{epic-id}.json`

Example: `kanban-epic-0-001.json` for EPIC-0-001

## File Structure

Each epic kanban file contains:

```json
{
  "metadata": {
    "version": "2.0",
    "createdDate": "YYYY-MM-DD",
    "epicId": "EPIC-X-XXX",
    "epicTitle": "Epic Title",
    "epic": "Epic Category",
    "phase": 0-4,
    "description": "Description of this epic kanban",
    "originalEffort": 8,
    "brokenDownTasks": 6,
    "totalEffort": 9
  },
  "tasks": [
    {
      "id": "EPIC-X-XXX",
      "isParentEpic": true,
      // ... original epic task details
    },
    {
      "id": "EPIC-X-XXX-001",
      // ... first broken-down subtask
    },
    // ... more subtasks
  ]
}
```

## Epic Breakdown Reference

See `../KANBAN-BREAKDOWN-GUIDE.md` for the complete breakdown of all epics into 1-2 day tasks.

## Generating Epic Files

Use the script `../generate-epic-kanbans.py` to generate all epic kanban files, or create them manually following the structure above.

## Current Status

- ✅ EPIC-0-001: Account Registration (6 subtasks)
- ⏳ Other epics: To be generated
