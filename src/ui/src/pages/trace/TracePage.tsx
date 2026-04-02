import { useState, useEffect, useMemo } from 'react';
import { useParams, useNavigate } from 'react-router-dom';
import {
  ReactFlow,
  Controls,
  Background,
  BackgroundVariant,
  type Node,
  type Edge,
  type NodeTypes,
  type NodeProps,
  Handle,
  Position,
} from '@xyflow/react';
import '@xyflow/react/dist/style.css';
import dagre from '@dagrejs/dagre';
import { StateBadge } from '@/components/StateBadge';
import { shortType, shortId } from '@/utils/format';
import { LoadingState, ErrorState } from '@/components/PageState';
import { Briefcase, Mail, Layers } from 'lucide-react';
import type { TraceJobModel } from '@/types';
import * as api from '@/api';

const NODE_WIDTH = 220;
const NODE_HEIGHT = 72;
const GROUP_PADDING = 20;

function kindIcon(kind: number) {
  if (kind === 3) return <Layers className="h-4 w-4 text-muted-foreground" />;
  if (kind === 2) return <Mail className="h-4 w-4 text-muted-foreground" />;
  return <Briefcase className="h-4 w-4 text-muted-foreground" />;
}

function kindLabel(kind: number) {
  if (kind === 3) return 'Batch';
  if (kind === 2) return 'Message';
  return 'Job';
}


// Is this a parallel child (batch child or message handler)?
function isParallelChild(child: TraceJobModel, parent: TraceJobModel | undefined): boolean {
  if (!parent) return false;
  // Batch → Job or Message → Job = parallel children
  return (parent.kind === 3 || parent.kind === 2) && child.kind === 1;
}

function TraceNode({ data }: NodeProps) {
  const navigate = useNavigate();
  const job = data as unknown as TraceJobModel;
  return (
    <div
      className="bg-card border rounded-lg px-3 py-2 shadow-sm cursor-pointer hover:border-primary transition-colors"
      style={{ width: NODE_WIDTH, minHeight: NODE_HEIGHT }}
      onClick={() => navigate(`/detail/${job.id}`)}
    >
      <Handle type="target" position={Position.Left} className="!bg-muted-foreground !w-2 !h-2" />
      <div className="flex items-center gap-2 mb-1">
        {kindIcon(job.kind)}
        <span className="text-xs text-muted-foreground">{kindLabel(job.kind)}</span>
        <span className="text-xs font-mono text-muted-foreground ml-auto">{shortId(job.id)}</span>
      </div>
      <div className="text-sm font-medium truncate">{shortType(job.type)}</div>
      <div className="flex items-center gap-2 mt-1">
        <StateBadge state={job.currentState} />
        {job.handlerType && <span className="text-xs text-muted-foreground truncate">{shortType(job.handlerType)}</span>}
      </div>
      <Handle type="source" position={Position.Right} className="!bg-muted-foreground !w-2 !h-2" />
    </div>
  );
}

function GroupNode({ data }: NodeProps) {
  const label = data.label as string;
  return (
    <div
      className="border-2 border-dashed border-muted-foreground/30 rounded-xl"
      style={{ width: '100%', height: '100%', position: 'relative' }}
    >
      <Handle type="target" position={Position.Left} className="!bg-muted-foreground !w-2 !h-2" />
      <span className="absolute -top-3 left-3 bg-background px-2 text-xs text-muted-foreground font-medium">
        {label}
      </span>
      <Handle type="source" position={Position.Right} className="!bg-muted-foreground !w-2 !h-2" />
    </div>
  );
}

const nodeTypes: NodeTypes = { traceNode: TraceNode, group: GroupNode };

function buildGraph(jobs: TraceJobModel[]): { nodes: Node[]; edges: Edge[] } {
  const jobMap = new Map(jobs.map(j => [j.id, j]));
  const nodes: Node[] = [];
  const edges: Edge[] = [];

  // Identify containers (batches/messages that have parallel children in this trace)
  const containers = new Map<string, TraceJobModel[]>(); // parentId → children
  for (const job of jobs) {
    if (job.parentJobId && jobMap.has(job.parentJobId)) {
      const parent = jobMap.get(job.parentJobId)!;
      if (isParallelChild(job, parent)) {
        if (!containers.has(job.parentJobId)) containers.set(job.parentJobId, []);
        containers.get(job.parentJobId)!.push(job);
      }
    }
  }

  // Track which children are in a container (skip individual edges for them)
  const childrenInContainer = new Set<string>();
  for (const children of containers.values()) {
    for (const c of children) childrenInContainer.add(c.id);
  }

  // Create job nodes
  for (const job of jobs) {
    nodes.push({
      id: job.id,
      type: 'traceNode',
      data: job as unknown as Record<string, unknown>,
      position: { x: 0, y: 0 },
    });

    if (job.parentJobId && jobMap.has(job.parentJobId)) {
      // Skip individual edges for batch/message children — one edge to the group instead
      if (!childrenInContainer.has(job.id)) {
        edges.push({
          id: `p-${job.id}`,
          source: job.parentJobId,
          target: job.id,
          type: 'smoothstep',
          style: { strokeWidth: 2 },
        });
      }
    }

    // SpawnedBy edge — only show if node has no parent edge and isn't in a container
    if (job.spawnedByJobId && jobMap.has(job.spawnedByJobId)
      && job.spawnedByJobId !== job.parentJobId
      && !job.parentJobId
      && !childrenInContainer.has(job.id)) {
      edges.push({
        id: `s-${job.id}`,
        source: job.spawnedByJobId,
        target: job.id,
        type: 'smoothstep',
        style: { strokeDasharray: '5,5', stroke: '#94a3b8', strokeWidth: 1 },
        animated: true,
      });
    }
  }

  // Auto-layout with dagre (all nodes flat, then compute group bounds)
  const g = new dagre.graphlib.Graph();
  g.setDefaultEdgeLabel(() => ({}));
  g.setGraph({ rankdir: 'LR', nodesep: 40, ranksep: 120 });

  for (const node of nodes) {
    g.setNode(node.id, { width: NODE_WIDTH, height: NODE_HEIGHT });
  }
  // Add all parent→child relationships to dagre for layout (including container children)
  for (const job of jobs) {
    if (job.parentJobId && jobMap.has(job.parentJobId)) {
      g.setEdge(job.parentJobId, job.id);
    }
  }
  // For nodes without a parent edge, use spawned-by edge for layout
  const nodesWithParentEdge = new Set(jobs.filter(j => j.parentJobId && jobMap.has(j.parentJobId)).map(j => j.id));
  for (const edge of edges) {
    if (edge.id.startsWith('s-') && !nodesWithParentEdge.has(edge.target)) {
      g.setEdge(edge.source, edge.target);
    }
  }

  dagre.layout(g);

  // Apply positions
  for (const node of nodes) {
    const pos = g.node(node.id);
    node.position = { x: pos.x - NODE_WIDTH / 2, y: pos.y - NODE_HEIGHT / 2 };
  }

  // Create group nodes around container children (batch/message node stays outside)
  for (const [parentId, children] of containers) {
    const parent = jobMap.get(parentId)!;
    const childIds = children.map(c => c.id);
    const positions = childIds.map(id => {
      const n = nodes.find(n => n.id === id)!;
      return n.position;
    });

    const minX = Math.min(...positions.map(p => p.x)) - GROUP_PADDING;
    const minY = Math.min(...positions.map(p => p.y)) - GROUP_PADDING - 10;
    const maxX = Math.max(...positions.map(p => p.x)) + NODE_WIDTH + GROUP_PADDING;
    const maxY = Math.max(...positions.map(p => p.y)) + NODE_HEIGHT + GROUP_PADDING;

    const groupId = `group-${parentId}`;
    nodes.push({
      id: groupId,
      type: 'group',
      data: { label: `${parent.kind === 3 ? 'Batch' : 'Message'} (${children.length} jobs)` },
      position: { x: minX, y: minY },
      style: { width: maxX - minX, height: maxY - minY, zIndex: -1 },
    });

    // Make child nodes relative to group
    for (const id of childIds) {
      const node = nodes.find(n => n.id === id)!;
      node.parentId = groupId;
      node.position = { x: node.position.x - minX, y: node.position.y - minY };
    }

    // Single edge from batch/message node to the group
    edges.push({
      id: `g-${parentId}`,
      source: parentId,
      target: groupId,
      type: 'smoothstep',
      style: { stroke: '#94a3b8', strokeWidth: 1 },
    });
  }

  return { nodes, edges };
}

export default function TracePage() {
  const { traceId } = useParams<{ traceId: string }>();
  const [jobs, setJobs] = useState<TraceJobModel[] | null>(null);
  const [error, setError] = useState<string | null>(null);

  useEffect(() => {
    if (traceId) {
      api.getTraceTree(traceId).then(setJobs).catch(() => setError('Unable to load trace'));
    }
  }, [traceId]);

  const { nodes, edges } = useMemo(() => {
    if (!jobs || jobs.length === 0) return { nodes: [], edges: [] };
    return buildGraph(jobs);
  }, [jobs]);

  if (error) return <ErrorState message={error} />;
  if (!jobs) return <LoadingState />;

  return (
    <div>
      <div className="flex items-center gap-4 mb-4">
        <h1 className="text-2xl font-bold">Trace {shortId(traceId ?? '')}</h1>
        <span className="text-sm text-muted-foreground">{jobs.length} jobs</span>
      </div>
      <div className="rounded-md border bg-card" style={{ height: 'calc(100vh - 12rem)' }}>
        <ReactFlow
          nodes={nodes}
          edges={edges}
          nodeTypes={nodeTypes}
          fitView
          fitViewOptions={{ padding: 0.2 }}
          minZoom={0.1}
          maxZoom={2}
          proOptions={{ hideAttribution: true }}
        >
          <Controls className="!bg-card !border !border-border !shadow-sm [&>button]:!bg-card [&>button]:!border-border [&>button]:!fill-foreground [&>button:hover]:!bg-accent" />
          <Background variant={BackgroundVariant.Dots} gap={16} size={1} />
          <div className="absolute top-3 right-3 bg-card border rounded-lg px-4 py-3 text-xs space-y-2 shadow-sm z-10">
            <div className="font-medium text-sm mb-2">Legend</div>
            <div className="flex items-center gap-2">
              <Briefcase className="h-3.5 w-3.5 text-muted-foreground" />
              <span>Job</span>
              <Mail className="h-3.5 w-3.5 text-muted-foreground ml-3" />
              <span>Message</span>
              <Layers className="h-3.5 w-3.5 text-muted-foreground ml-3" />
              <span>Batch</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-8 h-0 border-t-2 border-foreground" />
              <span>Continuation</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-8 h-0 border-t border-dashed border-muted-foreground" />
              <span>Spawned by</span>
            </div>
            <div className="flex items-center gap-2">
              <div className="w-8 h-4 border-2 border-dashed border-muted-foreground/30 rounded" />
              <span>Batch/Message children</span>
            </div>
          </div>
        </ReactFlow>
      </div>
    </div>
  );
}
