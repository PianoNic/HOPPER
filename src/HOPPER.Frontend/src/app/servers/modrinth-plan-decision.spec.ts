import { describe, expect, it } from 'vitest';
import { needsADecision } from './modrinth-plan-dialog';
import { ModrinthInstallPlanDto } from '../api/model/modrinthInstallPlanDto';
import { ModrinthPlanNodeDto } from '../api/model/modrinthPlanNodeDto';
import { PlanNodeStatus } from '../api/model/planNodeStatus';

function node(status: number = PlanNodeStatus.New): ModrinthPlanNodeDto {
  return {
    versionId: `v-${status}-${Math.random()}`,
    projectId: 'p',
    title: 'JEI',
    fileName: 'jei.jar',
    fileSize: 1,
    status,
    requiredBy: [],
  } as unknown as ModrinthPlanNodeDto;
}

function plan(over: Partial<ModrinthInstallPlanDto> = {}): ModrinthInstallPlanDto {
  return {
    nodes: [node()],
    optional: [],
    embedded: [],
    incompatible: [],
    unresolvable: [],
    warnings: [],
    blocked: false,
    addCount: 1,
    addSize: 1,
  } as unknown as ModrinthInstallPlanDto;
}

describe('needsADecision', () => {
  it('does not ask when every file is new and there is nothing to report', () => {
    expect(needsADecision(plan())).toBe(false);
  });

  it('asks when something is optional, which is the only real choice on offer', () => {
    expect(needsADecision({ ...plan(), optional: [node()] })).toBe(true);
  });

  it('asks when the plan is blocked by an incompatibility', () => {
    expect(needsADecision({ ...plan(), blocked: true })).toBe(true);
  });

  it('asks when a mod is already on the server, because replacing it is a decision', () => {
    expect(needsADecision({ ...plan(), nodes: [node(PlanNodeStatus.New), node(99)] })).toBe(true);
  });

  it.each([
    ['warnings', { warnings: ['Modrinth returned an older build'] }],
    ['unresolvable entries', { unresolvable: [{}] }],
    ['embedded jars', { embedded: [{}] }],
  ])('asks when the plan carries %s, because they are worth seeing first', (_, over) => {
    expect(needsADecision({ ...plan(), ...over } as ModrinthInstallPlanDto)).toBe(true);
  });

  it('asks rather than installing nothing when the plan is empty', () => {
    expect(needsADecision({ ...plan(), nodes: [] })).toBe(true);
  });
});
