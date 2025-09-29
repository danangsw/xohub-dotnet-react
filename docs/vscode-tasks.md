# VS Code Tasks - Complete Guide

## Overview

VS Code Tasks allow you to run scripts and tools as part of your development workflow directly from the editor. This project includes a comprehensive set of tasks for building, testing, and deploying the TicTacToe application.

## Available Tasks

### Backend (.NET) Tasks

| Task | Description | Command |
|------|-------------|---------|
| `build-backend` | Build ASP.NET Core project | `dotnet build` |
| `clean-backend` | Clean build artifacts | `dotnet clean` |
| `restore-backend` | Restore NuGet packages | `dotnet restore` |
| `watch-backend` | Auto-rebuild on file changes | `dotnet watch run` |
| `test-backend` | Run unit tests | `dotnet test` |
| `test-backend-watch` | Watch and auto-run tests | `dotnet watch test` |
| `coverage-backend` | Run tests with code coverage | `dotnet test --collect:"XPlat Code Coverage"` |
| `coverage-report` | Generate HTML coverage report | `reportgenerator` |
| `coverage-full` | Run tests + generate report | Combined task |

### Frontend (React) Tasks

| Task | Description | Command |
|------|-------------|---------|
| `install-frontend` | Install npm dependencies | `npm install` |
| `build-frontend` | Build for production | `npm run build` |
| `dev-frontend` | Start development server | `npm run dev` |
| `test-frontend` | Run tests | `npm test` |
| `test-frontend-watch` | Watch and auto-run tests | `npm run test:watch` |
| `lint-frontend` | Run ESLint | `npm run lint` |
| `lint-fix-frontend` | Auto-fix linting issues | `npm run lint:fix` |
| `format-frontend` | Format with Prettier | `npm run format` |

### Docker Tasks

| Task | Description | Command |
|------|-------------|---------|
| `docker-build` | Build containers | `docker-compose build` |
| `docker-up` | Start containers (detached) | `docker-compose up -d` |
| `docker-up-build` | Build and start with logs | `docker-compose up --build` |
| `docker-down` | Stop containers | `docker-compose down` |
| `docker-logs` | Follow container logs | `docker-compose logs -f` |

### Combined Tasks

| Task | Description | Dependencies |
|------|-------------|--------------|
| `build-full-stack` | Build both backend + frontend | `restore-backend`, `build-backend`, `install-frontend`, `build-frontend` |
| `dev-full-stack` | Start both in development mode | `watch-backend`, `dev-frontend` |
| `test-full-stack` | Run all tests | `test-backend`, `test-frontend` |
| `clean-all` | Clean all build artifacts | `clean-backend` |

## How to Run Tasks

### Method 1: Command Palette

1. Press `Ctrl+Shift+P` (or `Cmd+Shift+P` on Mac)
2. Type "Tasks: Run Task"
3. Select the desired task from the list

### Method 2: Keyboard Shortcuts

- `Ctrl+Shift+B`: Run default build task (`docker-up-build`)
- `Ctrl+Shift+P` → "Tasks: Run Test Task": Run test tasks

### Method 3: Terminal Menu

- Click the **Terminal** menu
- Select **Run Task**
- Choose from the task list

### Method 4: Status Bar

- Look for the **Tasks** button in the status bar
- Click to see available tasks

## Task Configuration

### Task Groups

Tasks are organized into groups for easy access:

- **build**: Build and compilation tasks
- **test**: Testing and coverage tasks

### Task Properties

#### Basic Properties

```json
{
  "label": "task-name",
  "command": "executable",
  "type": "process" | "shell",
  "args": ["arg1", "arg2"],
  "options": {
    "cwd": "${workspaceFolder}/subfolder"
  }
}
```

#### Advanced Properties

```json
{
  "group": "build",
  "presentation": {
    "echo": true,
    "reveal": "always",
    "focus": false,
    "panel": "shared"
  },
  "problemMatcher": "$msCompile",
  "isBackground": false
}
```

### Environment Variables

```json
{
  "options": {
    "env": {
      "ASPNETCORE_ENVIRONMENT": "Development",
      "ASPNETCORE_URLS": "http://localhost:5000"
    }
  }
}
```

## Task Dependencies

### Sequential Execution

```json
{
  "label": "build-and-test",
  "dependsOn": ["build-backend", "test-backend"],
  "dependsOrder": "sequence"
}
```

### Parallel Execution

```json
{
  "label": "dev-full-stack",
  "dependsOn": ["watch-backend", "dev-frontend"],
  "dependsOrder": "parallel"
}
```

## Background Tasks

Tasks marked with `"isBackground": true` run continuously:

- `watch-backend`: Auto-rebuilds on file changes
- `dev-frontend`: Runs development server
- `test-backend-watch`: Auto-runs tests on changes
- `docker-logs`: Follows container logs

## Problem Matchers

Tasks use problem matchers to parse errors and warnings:

- `$msCompile`: .NET compiler errors
- `$tsc`: TypeScript compiler errors
- `$eslint-stylish`: ESLint output

## Code Coverage Workflow

### Quick Coverage Check

```bash
# Run tests with coverage collection
coverage-backend

# Generate HTML report
coverage-report
```

### Complete Coverage Analysis

```bash
# Run full coverage workflow
coverage-full
```

### Coverage Report Location

- **HTML Report**: `tests/TicTacToe.Server.Tests/TestResults/CoverageReport/index.html`
- **Raw Data**: `tests/TicTacToe.Server.Tests/TestResults/*/coverage.cobertura.xml`

## Development Workflows

### Full Development Setup

```bash
# Start both backend and frontend in development mode
dev-full-stack
```

### Build for Production

```bash
# Build entire stack
build-full-stack

# Or build with Docker
docker-up-build
```

### Testing Workflow

```bash
# Run all tests
test-full-stack

# Or run with coverage
coverage-full
```

## Custom Keybindings

Add to `keybindings.json` for quick access:

```json
[
  {
    "key": "ctrl+shift+t",
    "command": "workbench.action.tasks.runTask",
    "args": "test-backend"
  },
  {
    "key": "ctrl+shift+d",
    "command": "workbench.action.tasks.runTask",
    "args": "dev-full-stack"
  }
]
```

## Task Output and Monitoring

### Terminal Panel

- Shows real-time command output
- Background tasks run in dedicated terminals
- Tasks can be reused or run in new terminals

### Problems Panel

- Compiler errors and warnings
- Parsed from task output using problem matchers
- Click to navigate to source locations

### Task Management

- View running tasks in Terminal panel
- Stop background tasks with trash icon
- Restart tasks as needed

## Best Practices

### Task Organization

1. Use descriptive labels
2. Group related tasks together
3. Set appropriate default tasks
4. Use dependencies for complex workflows

### Performance

1. Use background tasks for watchers
2. Configure appropriate problem matchers
3. Set proper working directories
4. Use parallel execution when possible

### Error Handling

1. Configure problem matchers for error parsing
2. Use appropriate presentation settings
3. Set up proper environment variables
4. Handle task failures gracefully

## Troubleshooting

### Common Issues

#### Task not found

- Check `tasks.json` syntax
- Ensure task label is correct
- Restart VS Code

### Command not found

- Verify executable is in PATH
- Check working directory
- Ensure dependencies are installed

#### Background task not stopping

- Use Terminal panel to stop manually
- Check task configuration
- Restart VS Code

### Debug Tasks

1. Run task manually in terminal first
2. Check task output in Terminal panel
3. Verify environment variables
4. Test with simplified commands

## Advanced Configuration

### Custom Tasks

Add new tasks to `.vscode/tasks.json`:

```json
{
  "label": "custom-task",
  "command": "your-command",
  "args": ["--flag", "value"],
  "options": {
    "cwd": "${workspaceFolder}",
    "env": {
      "CUSTOM_VAR": "value"
    }
  },
  "group": "build",
  "presentation": {
    "echo": true,
    "reveal": "always"
  }
}
```

### Task Variables

- `${workspaceFolder}`: Project root
- `${file}`: Current file
- `${relativeFile}`: Relative path to current file
- `${workspaceFolderBasename}`: Project name

### Conditional Tasks

Use `runOptions` for platform-specific tasks:

```json
{
  "runOptions": {
    "runOn": "folderOpen"
  }
}
```

## Integration with Other Tools

### Git Integration

- Pre-commit hooks can run tasks
- CI/CD pipelines can use same commands
- Tasks ensure consistent development environment

### Docker Integration

- Tasks mirror docker-compose commands
- Consistent development and deployment
- Easy transition from local to containerized

### Testing Integration

- Coverage tasks integrate with CI/CD
- Test results available in VS Code
- Automated testing workflows

## Conclusion

VS Code Tasks provide a powerful way to manage development workflows. This project's comprehensive task configuration enables efficient development, testing, and deployment of the TicTacToe application.

Key benefits:

- **Consistency**: Same commands across team members
- **Efficiency**: Keyboard shortcuts and quick access
- **Integration**: Works with VS Code's UI and other extensions
- **Automation**: Complex workflows with dependencies
- **Monitoring**: Real-time output and error tracking

For more information, see the [VS Code Tasks documentation](https://code.visualstudio.com/docs/editor/tasks).</content>
<parameter name="filePath">f:\programming\csharp\xohub-dotnet-react\docs\vscode-tasks.md
