# User Acceptance Testing

Phase 8 calls for user acceptance testing. This script turns the specification's Definition of Done
(Part 12) and the final directive into tasks that DOF staff perform on the deployed system with real
work. **It must be carried out by DOF people (management, producers, team members), not by the
developers.** Automated tests already cover each item (listed for reference). UAT confirms the
system fits how DOF actually works.

**How to run it:** one session per role (administrator, production manager, producer, team member),
on the production or a staging server, each person on their own computer. Record pass/fail and
comments in the sign-off table. Raise every failure or confusing step as an issue.

| # | Definition of Done | Scenario for testers | Who | Automated coverage |
|---|---|---|---|---|
| 1 | Create and manage team accounts | Administrator creates accounts for two staff, gives one the Producer role, deactivates and reactivates one; the person signs in and changes their password | Admin | `UserManagementTests`, `AuthTests` |
| 2 | See all organisational activities in one calendar | Open the calendar in month, week, day and agenda; confirm meetings, milestones, task deadlines, recordings, deadlines and releases all appear; filter by person and by show | Everyone | `CalendarTests`, `ShowTests`, `ReleaseTests`, calendar UI tests |
| 3 | Assign responsibilities to individual people | Assign a task with a primary person, a milestone, an episode editor and a release owner; each person sees it on their dashboard | Manager, Producer | `WorkTests`, `ShowTests` |
| 4 | Create and track projects | Create a project with milestones and tasks, move it to Active, complete a milestone, view its timeline and history | Manager | `WorkTests`, `WorkUiTests` |
| 5 | Manage recurring shows and episodes | Create a recurring show with two seasons and several episodes; add a one-off special | Producer | `ShowTests`, `ShowUiTests` |
| 6 | Record when an episode was produced | Schedule a recording, then mark the episode Recorded; check the recorded time and the timeline | Producer | `ShowTests` |
| 7 | Schedule when it will air or be published | Schedule a release time for the episode; see it on the calendar and on "Upcoming releases" | Producer | `ReleaseTests` |
| 8 | Schedule independent releases across different platforms | Schedule YouTube, Spotify and Television at different times; publish one and confirm the others are unchanged | Producer | `ReleaseTests.One_episode_has_independent_release_dates_and_statuses_across_channels` |
| 9 | Receive automated reminders | Set default reminders; with the app **closed**, wait for a reminder by e-mail (if configured); reopen and see it on the bell; reschedule the item and see the reminder move | Everyone | `ReminderTests` |
| 10 | Identify overdue projects, tasks and releases | Let a task and a release pass their time; see them flagged on the dashboard, in Releases → Overdue and in the Overdue tasks report; managers receive the escalation after 24 h | Manager | `ReminderTests`, `ReleaseTests`, `ReportTests` |
| 11 | Confirm when content actually goes live | Confirm a release with its actual time and link; the episode becomes Partly published / Published; the scheduled time is kept | Producer | `ReleaseTests.Publication_needs_confirmation_and_keeps_both_scheduled_and_actual_times` |
| 12 | View historical production activity | Open a project, episode and release history; run the weekly/monthly reports and publication history; export CSV and open it in a spreadsheet | Manager | `ReportTests`, `ReportUiTests` |
| 13 | Access shared information from multiple computers | Two people on different computers edit the same task; the second save is rejected with a clear "modified by someone else" message; both see the same data after reloading | Two people | Version checks in every update test (409) |
| — | Final directive: management understands, at any time, what is being worked on, what is due next, who is responsible, what is delayed and what goes live | A manager opens the app cold and answers those five questions from the dashboard, calendar and reports within five minutes | Manager | Dashboard tests |

Also check during the sessions: the desktop app on macOS **and** Windows, light and dark themes,
performance of the dashboard, calendar and search on the office network (targets in spec Part 9), and
signing in again after the server restarts.

## Sign-off

| Role | Name | Date | Result | Issues raised |
|---|---|---|---|---|
| Administrator | | | | |
| Production Manager | | | | |
| Producer | | | | |
| Team Member | | | | |

Go-live requires every row signed and no open blocking issues.
