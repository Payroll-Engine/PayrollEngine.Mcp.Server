# MCP Server — Test Prompts: Core Examples

**Isolation:** `MultiTenant` — all tenants accessible  
**Loaded examples:** all examples from the `PayrollEngine` core repository  
**Purpose:** Exploratory prompts to verify tool coverage, data quality, and natural-language query behaviour

---

## System & Tenant Overview

```
List all tenants.
```

```
What tenants are available and how many employees does each one have?
```

```
Show me all users in StartTenant.
```

---

## HR — StartTenant

Tenant `StartTenant` · Employee `mario.nuñez@foo.com` · Division `StartDivision`

```
Show me the employees of StartTenant.
```

```
What case values does mario.nuñez@foo.com have in StartTenant?
```

```
What was mario.nuñez@foo.com's salary on 2025-06-01 in StartTenant?
```

```
What changed in mario.nuñez@foo.com's data in StartTenant — who changed what and when?
```

---

## Payroll & Results — StartTenant

```
What payrolls exist in StartTenant?
```

```
What wage types are effective in the StartPayroll payroll of StartTenant?
```

```
Show me all payroll results for mario.nuñez@foo.com in StartTenant.
```

```
What was mario.nuñez@foo.com's net pay in January 2025 in StartTenant?
```

---

## HR & Results — Report.Tenant

Tenant `Report.Tenant` · Payroll `Report.Payroll` · Employees: `johnson.bob`, `miller.alice`, `garcia.carlos`  
Payruns: January–December 2025, January 2026

```
Show me all employees of Report.Tenant.
```

```
What are Alice Miller's current case values in the Report.Payroll of Report.Tenant?
```

```
Show me all payroll results for miller.alice@example.com in Report.Tenant for 2025.
```

```
Compare the gross wage of all three employees — johnson.bob, miller.alice, and garcia.carlos — across all payrun periods in Report.Tenant.
```

```
Alice had a retroactive wage correction for January and February 2025. What was entered and when?
```

```
What was the social security rate in Report.Tenant on 2024-06-01? And what is it now?
```

```
Show me the last 3 payrun jobs in Report.Tenant ordered by date.
```

---

## Retro Corrections — RetroPayroll

Tenant `RetroPayroll` · Division `RetroPayroll.Engineering`  
Employees: `emma.bauer@retropayroll.com`, `ben.kowalski@retropayroll.com`  
Payruns: January–April 2024 with retro jobs in March and April

```
Show me the employees of RetroPayroll.
```

```
Emma Bauer had a retroactive salary adjustment in March 2024. What were the BaseSalary deltas for January and February?
```

```
What were emma.bauer@retropayroll.com's payroll results for March 2024 in RetroPayroll including retro corrections?
```

```
Compare the gross income of emma.bauer and ben.kowalski across all four payrun periods in RetroPayroll.
```

```
Ben Kowalski received a bonus. When was it entered, for which period, and how did it appear in the March payrun?
```

---

## Temporal Perspectives

These prompts verify the `get_case_time_values` tool across the three temporal perspectives  
(historical, current knowledge, forecast). See [CaseValueTemporalPerspectives.md](CaseValueTemporalPerspectives.md).

```
What was miller.alice@example.com's monthly wage on 2025-01-15 in Report.Tenant — as known on that exact date, excluding later corrections?
```

```
What do we know today about Alice's monthly wage for January 2025 in Report.Tenant, including any corrections entered since?
```

```
What was emma.bauer@retropayroll.com's employment level on 2024-02-15 in RetroPayroll as of that date, excluding later retroactive changes?
```

---

## Cross-Tenant Queries

```
Which tenants have run payrolls in 2025? List the payrun job counts per tenant.
```

```
Show me all payrun jobs across all tenants ordered by date descending, limit 10.
```

```
Which employees across all tenants have a salary above 6000?
```
