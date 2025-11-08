# OwnPlanner MCP Tools Reference

This document provides a reference for all available MCP tools in OwnPlanner.

## TaskList Tools

### CreateTaskList
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

### GetTaskList
Get a task list by ID.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### ListTaskLists
List all task lists.

**Parameters:**
- `includeArchived` (bool, optional, default: false) - Include archived lists

### UpdateTaskList
Update a task list's properties.

**Parameters:**
- `id` (Guid, required) - The ID of the task list
- `title` (string, optional) - New title
- `description` (string, optional) - New description
- `color` (string, optional) - New color

### ArchiveTaskList
Archive a task list.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### UnarchiveTaskList
Unarchive a task list.

**Parameters:**
- `id` (Guid, required) - The ID of the task list

### DeleteTaskList
Delete a task list. Tasks in the list will be orphaned (moved to no list).

**Parameters:**
- `id` (Guid, required) - The ID of the task list

---

## TaskItem Tools

### CreateTask
Create a new task.

**Parameters:**
- `title` (string, required) - The title of the task
- `description` (string, optional) - Description of the task
- `dueAt` (string, optional) - Due date in ISO format

**Example:**
```json
{
  "title": "Buy milk",
  "description": "Get 2% milk from the store",
  "dueAt": "2025-11-08T18:00:00Z"
}
```

### GetTask
Get a task by ID.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### ListTasks
List all tasks.

**Parameters:**
- `includeCompleted` (bool, optional, default: true) - Include completed tasks

### ListTasksByList
List tasks by task list ID.

**Parameters:**
- `taskListId` (string, optional) - The ID of the task list (null for tasks without a list)
- `includeCompleted` (bool, optional, default: true) - Include completed tasks

**Example to get orphaned tasks:**
```json
{
  "taskListId": null,
  "includeCompleted": true
}
```

### AssignTaskToList
Assign a task to a list.

**Parameters:**
- `taskId` (Guid, required) - The ID of the task
- `taskListId` (string, optional) - The ID of the task list (null to unassign)

**Example to unassign:**
```json
{
  "taskId": "12345678-1234-1234-1234-123456789012",
  "taskListId": null
}
```

### CompleteTask
Mark a task as complete.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### ReopenTask
Reopen a completed task.

**Parameters:**
- `id` (Guid, required) - The ID of the task

### DeleteTask
Delete a task.

**Parameters:**
- `id` (Guid, required) - The ID of the task

---

## Typical Workflows

### Create a Shopping List with Tasks
1. Create a list: `CreateTaskList("Shopping", "Weekly groceries", "#00FF00")`
2. Create tasks: `CreateTask("Buy milk")`, `CreateTask("Buy bread")`
3. Assign tasks: `AssignTaskToList(taskId, listId)`

### View All Tasks in a List
1. Get the list ID: `ListTaskLists()`
2. Get tasks: `ListTasksByList(listId, false)` // Active tasks only

### Archive Completed Lists
1. Complete all tasks in list: `CompleteTask(taskId)`
2. Archive the list: `ArchiveTaskList(listId)`

### Work with "Inbox" Tasks
- List tasks without a list: `ListTasksByList(null, false)`
- Move task to a list: `AssignTaskToList(taskId, listId)`
