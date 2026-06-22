# SMT Agent

Local delayed-data SMT dashboard.

## Run Locally

Backend:

```powershell
dotnet run --project src/SMTAgent.Api --urls http://localhost:5000
```

Frontend:

```powershell
cd web
npm install
npm run dev
```

Open `http://localhost:5173`.

The frontend is UI-only. Strategy logic, Yahoo delayed data, SMT detection, focused NQ 1m analysis, BOS/FVG/IFVG, the 0/0.5/1 box, and mock SL/TP calculations are served by the C# backend.
