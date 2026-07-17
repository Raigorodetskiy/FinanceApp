# FinanceApp

Finance portfolio management application with ASP.NET Core 8 Backend and React Frontend.

## Backend

The backend is built with ASP.NET Core 8 and runs at `http://173.249.42.11:5000`.

### Requirements
- .NET 8 SDK
- MySQL / MariaDB

### Run
```bash
cd FinanceApp.API
dotnet run
```

---

## Frontend

A React + TypeScript SPA that connects to the backend API.

### Technologies
- React 18, TypeScript, Vite
- Ant Design 5, Recharts, Axios, Day.js

### Configuration

Copy `.env.example` to `.env.local` and adjust as needed:
```bash
cp .env.example .env.local
```

By default the frontend connects to `http://173.249.42.11:5000/api`. Set `VITE_API_BASE_URL` in `.env.local` to override.

### Install
```bash
cd FinanceApp.Frontend
npm install
```

### Run
```bash
npm run dev
```

The app will be available at `http://localhost:3000`

### Build
```bash
npm run build
```
