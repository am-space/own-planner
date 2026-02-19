# OwnPlanner Web Client

A modern React + TypeScript + MUI web client for the OwnPlanner application.

## Features

- 🔐 **Authentication System**
  - User Registration (Email, Username, Password)
  - User Login (Email & Password)
  - Protected Routes
  - Persistent Authentication with Cookies
  - User status display in header

- 🎨 **User Interface**
  - Material-UI (MUI 6) components
  - **Theme Switching** (Light, Dark, and System mode)
  - Responsive layout for desktop and mobile
  - Clean, premium design with micro-animations
  - **About** and **Terms of Service** dialogs

- 💬 **AI Chat Interface**
  - **Markdown Support** (GFM, tables, code blocks with syntax highlighting)
  - Real-time message display with "thinking" states
  - **Suggested Prompts** for ease of use
  - Chat session management (Clear chat)
  - Responsive message feed with differentiation between User and Assistant

## Tech Stack

- **React 19** - UI Framework
- **TypeScript** - Type Safety
- **Material-UI (MUI) 6** - Component Library
- **React Router 7** - Navigation
- **Vite 7** - Build Tool
- **React Markdown** - Content rendering
- **Remark GFM** - GitHub Flavored Markdown support

## Getting Started

### Prerequisites

- Node.js (v18 or higher)
- npm or yarn

### Installation

```bash
# Install dependencies
npm install
```

### Running the Application

```bash
# Development mode
npm run dev

# Build for production
npm run build

# Preview production build
npm run preview
```

The application will run on `https://localhost:56404` (or the port specified in environment variables).

## Project Structure

```
src/
├── components/          # Reusable UI components
│   ├── AboutDialog.tsx
│   ├── ProtectedRoute.tsx
│   └── TermsOfServiceDialog.tsx
├── contexts/           # React contexts for state management
│   ├── AuthContext.tsx
│   ├── ThemeContext.tsx
│   └── ThemeContextProvider.tsx
├── pages/              # Page components
│   ├── LoginPage.tsx
│   ├── RegisterPage.tsx
│   └── ChatPage.tsx
├── services/           # API services
│   └── api.ts
├── types/              # TypeScript definitions
│   ├── api.types.ts
│   └── index.ts
├── App.tsx             # Main app component & routing
├── main.tsx            # App entry point
└── index.css           # Global styles & layout
```

## API Integration

The client communicates with the ASP.NET backend via REST API:

- `POST /api/auth/register` - Register new user
- `POST /api/auth/login` - Login user
- `POST /api/auth/logout` - Logout user
- `GET /api/auth/check` - Check authentication status
- `GET /api/auth/me` - Get current user info

## Routes

- `/` - Redirects to `/chat`
- `/login` - Login page
- `/register` - Registration page
- `/chat` - Chat interface (protected)

## Authentication Flow

1. User registers or logs in.
2. Backend sets HTTP-only cookie.
3. Client stores user info in `AuthContext`.
4. Protected routes check authentication status automatically.
5. API calls include `credentials: 'include'` to send cookies.

## MCP Integration (Planned)

The chat interface is prepared for deeper MCP (Model Context Protocol) integration:
- Tool execution through the assistant.
- Enhanced context awareness for planning.
- Knowledge base integration.

## Development Notes

### Hot Module Replacement (HMR)
Vite provides near-instant HMR during development.

### HTTPS in Development
The app uses HTTPS in development mode with certificates managed by the .NET dev-certs tool.

### Proxy Configuration
API calls are proxied to the backend server during development (configured in `vite.config.ts`).

## Building for Production

```bash
npm run build
```

The build output will be in the `dist/` directory, which is served by the ASP.NET backend.

## Troubleshooting

### HTTPS Certificate Issues
If you encounter certificate errors:
```bash
dotnet dev-certs https --clean
dotnet dev-certs https --trust
```

### Port Conflicts
Change the port in `vite.config.ts` if 56404 is already in use.


