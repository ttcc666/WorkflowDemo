import {
  applyConnection,
  branchLabel,
  canInsertOnEdge,
  checkConnection,
  checkSchema,
  cleanSchema,
  cleanSettings,
  cloneField,
  createEdge,
  createHistory,
  createNodeData,
  createStateCell,
  insertNodeOnEdge,
  layoutNodes,
  makeRoomBelow,
  NODE_WIDTH,
  NODE_HEIGHT,
  newFlowDefinition,
  newNodeId,
  toDefinition,
  toFlowElements,
  type BranchData,
  type FlowDefinition,
  type FlowEdge,
  type FlowNode,
  type FlowNodeData,
  type FlowSettings,
  type FormSchema,
  type NodeType,
  type NotificationRule,
} from "@approval/shared";

export interface DesignerState {
  nodes: FlowNode[];
  edges: FlowEdge[];
  form: FormSchema;
  settings: FlowSettings;
  notifications: NotificationRule[];
  selectedNodeId: string | null;
  selectedEdgeId: string | null;
  dirty: boolean;
  canUndo: boolean;
  canRedo: boolean;
  errors: string[];
  warnings: string[];
}
export function createDesignerModel(initial = newFlowDefinition()) {
  const history = createHistory();
  const elements = toFlowElements(initial);
  const state = createStateCell<DesignerState>({
    ...elements,
    form: cloneField(initial.form ?? { fields: [] }),
    settings: cloneField(initial.settings ?? {}),
    notifications: cloneField(initial.notifications ?? []),
    selectedNodeId: null,
    selectedEdgeId: null,
    dirty: false,
    canUndo: false,
    canRedo: false,
    errors: [],
    warnings: [],
  });
  function definition(): FlowDefinition {
    const current = state.value;
    const result = toDefinition(
      current.nodes,
      current.edges,
      current.notifications,
    );
    if (current.form.fields.length) result.form = cleanSchema(current.form);
    const settings = cleanSettings(current.settings);
    if (settings) result.settings = settings;
    return result;
  }
  const snapshot = () => JSON.stringify(definition());
  let saved = snapshot();
  history.reset(saved);
  function sync() {
    state.value = {
      ...state.value,
      dirty: snapshot() !== saved,
      canUndo: history.canUndo,
      canRedo: history.canRedo,
    };
  }
  function change(
    action: (draft: DesignerState) => void,
    key?: string,
    record = true,
  ) {
    const next = cloneField(state.value);
    action(next);
    state.value = next;
    if (record) history.record(snapshot(), key);
    sync();
  }
  function load(value: FlowDefinition, reset = true) {
    state.value = {
      ...state.value,
      ...toFlowElements(value),
      form: cloneField(value.form ?? { fields: [] }),
      settings: cloneField(value.settings ?? {}),
      notifications: cloneField(value.notifications ?? []),
      selectedNodeId: null,
      selectedEdgeId: null,
      errors: [],
      warnings: [],
    };
    if (reset) {
      saved = snapshot();
      history.reset(saved);
    }
    sync();
  }
  function insert(draft: DesignerState, nodeId: string, edgeId: string) {
    const source = draft.nodes.find(
      (node) =>
        node.id === draft.edges.find((edge) => edge.id === edgeId)?.source,
    );
    const edges = insertNodeOnEdge(draft.nodes, draft.edges, edgeId, nodeId);
    if (!edges) return false;
    draft.edges = edges;
    const node = draft.nodes.find((item) => item.id === nodeId);
    if (node && source)
      node.position = {
        x: source.position.x,
        y: Math.max(
          node.position.y,
          source.position.y + (source.dimensions?.height || 72) + 50,
        ),
      };
    draft.nodes = makeRoomBelow(draft.nodes, nodeId);
    return true;
  }
  return {
    state,
    definition,
    load,
    select: (nodeId: string | null, edgeId: string | null = null) =>
      change(
        (draft) => {
          draft.selectedNodeId = nodeId;
          draft.selectedEdgeId = edgeId;
        },
        undefined,
        false,
      ),
    markSaved(value = definition()) {
      saved = JSON.stringify(value);
      sync();
    },
    setCanvas(nodes: FlowNode[], edges: FlowEdge[], record = true) {
      change(
        (draft) => {
          draft.nodes = nodes;
          draft.edges = edges;
        },
        undefined,
        record,
      );
    },
    record() {
      history.record(snapshot());
      sync();
    },
    addNode(
      type: NodeType,
      center = { x: 300, y: 160 },
      edgeId?: string | null,
      stagger = true,
    ) {
      const id = newNodeId(
        type,
        state.value.nodes.map((node) => node.id),
      );
      const offset = stagger ? (state.value.nodes.length % 5) * 24 : 0;
      change((draft) => {
        const position = {
          x: center.x - NODE_WIDTH / 2 + offset,
          y: center.y - NODE_HEIGHT / 2 + offset,
        };
        if (stagger && !edgeId) {
          let collision: FlowNode | undefined;
          while (
            (collision = draft.nodes.find(
              (node) =>
                position.x <
                  node.position.x +
                    (node.dimensions?.width || NODE_WIDTH) +
                    12 &&
                position.x + NODE_WIDTH + 12 > node.position.x &&
                position.y <
                  node.position.y +
                    (node.dimensions?.height || NODE_HEIGHT) +
                    12 &&
                position.y + NODE_HEIGHT + 12 > node.position.y,
            ))
          ) {
            position.y =
              collision.position.y +
              (collision.dimensions?.height || NODE_HEIGHT) +
              24;
          }
        }
        draft.nodes.push({
          id,
          type: "flow-node",
          position,
          data: createNodeData(type),
          deletable: type !== "Start",
        });
        draft.selectedNodeId = id;
        draft.selectedEdgeId = null;
      });
      if (edgeId)
        change((draft) => {
          insert(draft, id, edgeId);
        });
      return id;
    },
    connect(source: string, target: string) {
      const error = checkConnection(
        state.value.nodes,
        state.value.edges,
        source,
        target,
      );
      if (error) return error;
      change((draft) => {
        draft.edges = applyConnection(draft.nodes, draft.edges, source, target);
      });
      return null;
    },
    insertOnEdge(nodeId: string, edgeId: string) {
      if (!canInsertOnEdge(state.value.nodes, state.value.edges, nodeId))
        return false;
      let result = false;
      change((draft) => {
        result = insert(draft, nodeId, edgeId);
      });
      return result;
    },
    updateNode(id: string, patch: Partial<FlowNodeData>) {
      const previous =
        state.value.nodes.find((node) => node.id === id)?.data?.actions ?? [];
      const structural =
        patch.actions &&
        (patch.actions.length !== previous.length ||
          patch.actions.some((step, index) => step.id !== previous[index]?.id));
      change(
        (draft) => {
          const node = draft.nodes.find((item) => item.id === id);
          if (node?.data) Object.assign(node.data, patch);
        },
        structural ? undefined : `node:${id}:${Object.keys(patch).join(",")}`,
      );
    },
    updateBranch(edgeId: string, patch: Partial<BranchData>) {
      change((draft) => {
        const edge = draft.edges.find((item) => item.id === edgeId);
        if (!edge?.data) return;
        if (patch.expression === null)
          for (const other of draft.edges)
            if (
              other.id !== edgeId &&
              other.source === edge.source &&
              other.data?.expression === null
            ) {
              other.data.expression = "";
              other.label = branchLabel(other.data);
            }
        edge.data = { ...edge.data, ...patch };
        edge.label = branchLabel(edge.data);
      }, `edge:${edgeId}`);
    },
    removeNode(id: string) {
      if (
        state.value.nodes.find((node) => node.id === id)?.data?.type === "Start"
      )
        return;
      change((draft) => {
        draft.nodes = draft.nodes.filter((node) => node.id !== id);
        draft.edges = draft.edges.filter(
          (edge) => edge.source !== id && edge.target !== id,
        );
        draft.selectedNodeId = null;
      });
    },
    removeEdge(id: string) {
      change((draft) => {
        draft.edges = draft.edges.filter((edge) => edge.id !== id);
        draft.selectedEdgeId = null;
      });
    },
    autoLayout: () =>
      change((draft) => {
        draft.nodes = layoutNodes(draft.nodes, draft.edges);
      }),
    updateForm: (form: FormSchema) =>
      change((draft) => {
        draft.form = form;
      }, "form"),
    updateSettings: (settings: FlowSettings) =>
      change((draft) => {
        draft.settings = settings;
      }, "settings"),
    updateNotifications: (notifications: NotificationRule[]) =>
      change((draft) => {
        draft.notifications = notifications;
      }, "notifications"),
    undo() {
      const value = history.undo();
      if (value) load(JSON.parse(value), false);
    },
    redo() {
      const value = history.redo();
      if (value) load(JSON.parse(value), false);
    },
    localErrors: () => [
      ...state.value.nodes
        .filter((node) => !node.data?.name.trim())
        .map((node) => `节点 '${node.id}' 未填写名称`),
      ...checkSchema(state.value.form),
    ],
    validation: (errors: string[], warnings: string[]) => {
      state.value = { ...state.value, errors, warnings };
    },
    edge: createEdge,
  };
}
export type DesignerModel = ReturnType<typeof createDesignerModel>;
