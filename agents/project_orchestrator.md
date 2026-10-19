# Project Orchestrator - Sumoner World

## Project-Specific Configuration
- **Engine**: Godot v4.7.2-stable (.NET)
- **Platform**: PC
- **Project**: Sumoner World

## Agent Team for This Project
- **Master Orchestrator** (`agents/master_orchestrator.md`)
- **Producer Agent** (`agents/producer_agent.md`)
- **Market Analyst** (`agents/market_analyst.md`)
- **Data Scientist** (`agents/data_scientist.md`)
- **Sr Game Designer** (`agents/sr_game_designer.md`)
- **Mid Game Designer** (`agents/mid_game_designer.md`)
- **Mechanics Developer** (`agents/mechanics_developer.md`)
- **Game Feel Developer** (`agents/game_feel_developer.md`)
- **Qa Agent** (`agents/qa_agent.md`)
- **Sr Game Artist** (`agents/sr_game_artist.md`)
- **Technical Artist** (`agents/technical_artist.md`)
- **Ui Ux Agent** (`agents/ui_ux_agent.md`)


## Project-Specific Workflow

### Current Phase
**Phase**: Pre-Production

### Next Steps
Continue with current phase objectives

## Engine-Specific Commands

### Godot Development Commands
```bash
# Create new Godot project
godot --editor --path ./source

# Run project
godot --path ./source

# Export for platform
godot --export "Platform" ./builds/game.exe --path ./source
```

## Project File Locations
- **Project Config**: `project-config.json`
- **Agents**: `agents/` (project-specific)
- **Documentation**: `documentation/`
- **Source**: `source/`

## Quick Actions

### Resume Development
```bash
# Navigate to project
cd projects/sumoner-worl

# Activate current phase agents
claude "Read agents/producer_agent.md and continue development"
```

### Check Project Status
```bash
claude "Read project-config.json and provide current status"
```

### Switch Development Phase
```bash
claude "Read agents/producer_agent.md and transition to [next-phase]"
```

## Troubleshooting

### Common Issues
- **Agent Conflicts**: Use project-specific agents in `agents/` folder
- **Engine Issues**: Check `godot_config.json` for best practices
- **Platform Issues**: Review platform-specific requirements

### Getting Help
1. Check project-specific agent documentation
2. Review engine configuration files
3. Consult project status in `project-config.json`
4. Use producer agent for coordination

---

## Project Development Rules (Sumoner World, mandatory)
Set by the Producer on 2026-10-19. These override any conflicting guidance above, including references to GDScript or Godot 4.4.1.
- **Engine/language:** Godot 4.7.2-stable .NET, C#. See `documentation/technical/architecture/ADR-001-engine-language-architecture.md`.
- **Rules:** R01–R57 in `project-config.json` → `development_rules`. Cite rule IDs in every deliverable.
- **Enforcement & Definition of Done:** `documentation/production/development_rules_enforcement.md`
- **Current tasks:** `documentation/production/milestones/M0_sprint_plan.md`
- Run `source/scripts/verify.sh` before reporting any code task as done (R55). Never merge, push, deploy or delete without explicit stakeholder approval (R56).
