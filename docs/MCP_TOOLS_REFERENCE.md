# OwnPlanner MCP Tools Reference

This document provides a reference for all available MCP tools in OwnPlanner.

## TaskList Tools

### tasklist_list_create
Create a new task list.

**Parameters:**
- `title` (string, required) - The title of the task list
- `description` (string, optional) - Description of the task list
- `color` (string, optional) - Color code for the list (e.g., "#FF5733")

**Example:**
```json
{
  "title": "Shopping List",
  "description": "Weekly groceries",
  "color": "#FF5733"
}
```

### tasklist_list_get
Get a task list by ID.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### tasklist_list_all
List all task lists.

**Parameters:**
- `includeArchived` (bool, optional, default: false) - Include archived lists

### tasklist_list_update
Update a task list's properties.

**Parameters:**
- `id` (Guid, required) - The ID of the task list
- `title` (string, optional) - New title
- `description` (string, optional) - New description
- `color` (string, optional) - New color

### tasklist_list_archive
Archive a task list.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### tasklist_list_unarchive
Unarchive a task list.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### tasklist_list_delete
Delete a task list.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

**Note:** This will fail if there are tasks assigned to this list. You must reassign or delete all tasks in the list before deleting the list.

---

## TaskItem Tools

### taskitem_item_create
Create a new task.

**Parameters:**
- `title` (string, required) - The title of the task
- `taskListId` (Guid, required) - The ID of the task list to assign this task to
- `description` (string, optional) - Description of the task
- `dueAt` (string, optional) - Due date in ISO format

**Example:**
```json
{
  "title": "Buy milk",
  "taskListId": "12345678-1234-1234-1234-123456789012",
  "description": "Get 2% milk from the store",
"dueAt": "2025-11-08T18:00:00Z"
}
```

### taskitem_item_get
Get a task by ID.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### taskitem_item_list_all
List all tasks.

**Parameters:**
- `includeCompleted` (bool, optional, default: true) - Include completed tasks

### taskitem_list_items
List tasks by task list ID.

**Parameters:**
- `taskListId` (Guid, required) - The ID of the task list
- `includeCompleted` (bool, optional, default: true) - Include completed tasks

**Example:**
```json
{
  "taskListId": "12345678-1234-1234-1234-123456789012",
  "includeCompleted": true
}
```

### taskitem_item_assign
Assign a task to a different list.

**Parameters:**
- `taskId` (Guid, required) - The ID of the task
- `taskListId` (Guid, required) - The ID of the task list to assign to

**Example:**
```json
{
  "taskId": "12345678-1234-1234-1234-123456789012",
  "taskListId": "87654321-4321-4321-4321-210987654321"
}
```

### taskitem_item_complete
Mark a task as complete.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### taskitem_item_reopen
Reopen a completed task.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### taskitem_item_delete
Delete a task.

**Parameters:**
- `id` (Guid, required) - The ID of the task

---

## Typical Workflows

### Create a Shopping List with Tasks
1. Create a list: `tasklist_list_create("Shopping", "Weekly groceries", "#00FF00")`
2. Create tasks with the list ID: `taskitem_item_create("Buy milk", listId)`, `taskitem_item_create("Buy bread", listId)`

### View All Tasks in a List
1. Get the list ID: `tasklist_list_all()`
2. Get tasks: `taskitem_list_items(listId, false)` // Active tasks only

### Archive Completed Lists
1. Complete all tasks in list: `taskitem_item_complete(taskId)`
2. Archive the list: `tasklist_list_archive(listId)`

### Reassign a Task to Another List
- Move task to a different list: `taskitem_item_assign(taskId, newListId)`
