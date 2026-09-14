---
inclusion: always
description: All LeagueSites work is tracked in Jira project LS; keep the board current as part of the work, without being asked
---

# Work tracking: Jira project LS

Work across the LeagueSites repos (Backend, Frontend, Ansible, Terraform) is
tracked in Jira Cloud: https://clfb.atlassian.net, project **LS**, board
"LS board" (To Do / In Progress / Done). Keep it current as part of doing the
work — the owner should never have to update the board by hand.

## The rule

- Starting a piece of work: find its issue or create one, move it to In Progress.
- Shipping it: move it to Done with a comment citing the commit id(s) and, for
  a deploy, the live build version and time.
- Follow-ups and new backlog items become issues (under the relevant epic when
  one exists), not entries in `.kiro/docs` or spec files. Specs record
  decisions and design; Jira records status.
- Style: short imperative summaries, often "Area: detail" ("Standings: add …");
  no labels; plain Tasks, an Epic only for a big theme.

## jira-cli

Expected at `~/.local/bin/jira`, configured by `~/.config/.jira/.config.yml`.
The API token lives in `~/.config/.jira/.env` (never in a repo, never printed);
the tool shell does not load `.bashrc`, so prefix every call with
`set -a; . ~/.config/.jira/.env; set +a; export PATH="$HOME/.local/bin:$PATH"`.

- Open issues: `jira issue list -q 'project = LS AND statusCategory != Done' --order-by created --plain --columns key,type,status,summary --no-headers` (no ORDER BY inside `-q`)
- Create: `jira issue create -tTask -s"Summary" -b"Body" [-P LS-<epic>] --no-input`
- Transition: `jira issue move LS-n "In Progress"` / `jira issue move LS-n Done`
- Comment: `jira issue comment add LS-n "text" --no-input`
