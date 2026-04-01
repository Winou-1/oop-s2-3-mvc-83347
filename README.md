# VGC College — Student & Course Management System
`oop-s2-1-mvc-YOURNUM`

## Description
ASP.NET Core MVC web application for managing students, courses, and academic
progress across three branches of Acme Global College.

## Tech Stack
- ASP.NET Core MVC (.NET 10)
- Entity Framework Core + SQLite
- ASP.NET Core Identity (RBAC)
- xUnit

## Setup
```bash
cd src/VgcCollege.Web
dotnet restore
dotnet run
```

The database is created and seeded automatically on first run.

## Seeded Accounts

| Role    | Email              | Password      |
|---------|--------------------|---------------|
| Admin   | admin@vgc.ie       | Admin@1234    |
| Faculty | faculty1@vgc.ie    | Faculty@1234  |
| Faculty | faculty2@vgc.ie    | Faculty@1234  |
| Student | student1@vgc.ie    | Student@1234  |
| Student | student2@vgc.ie    | Student@1234  |
| Student | student3@vgc.ie    | Student@1234  |
| Student | student4@vgc.ie    | Student@1234  |

## Running Tests
```bash
cd tests/VgcCollege.Tests
dotnet test
```

## Design Decisions
- SQLite used for portability (no SQL Server install required)
- `ResultsReleased` bool on `Exam` controls student visibility server-side
- Faculty data access filtered via `FacultyCourseAssignment` at query level
- `IsTutor` flag on faculty assignment controls contact details access
- Seed data covers 3 branches, 6 courses, 2 faculty, 4 students