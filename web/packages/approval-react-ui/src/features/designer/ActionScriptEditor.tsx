import { useEffect, useRef, useState } from "react";
import { basicSetup, EditorView } from "codemirror";
import { javascript, javascriptLanguage } from "@codemirror/lang-javascript";
import type { CompletionContext } from "@codemirror/autocomplete";
import { Button, ErrorState, Select } from "../../components/ui/primitives";
import { Alert, AlertDescription } from "../../components/shadcn/alert";

export function ActionScriptEditor({
  value,
  onChange,
  outputs,
  fields = [],
}: {
  value: string;
  onChange: (script: string) => void;
  outputs: string[];
  fields?: string[];
}) {
  const host = useRef<HTMLDivElement>(null);
  const editor = useRef<EditorView | null>(null);
  const callback = useRef(onChange);
  callback.current = onChange;
  const [error, setError] = useState<unknown>(null);
  const [formatting, setFormatting] = useState(false);
  const [notice, setNotice] = useState("");
  const revision = useRef(0);
  const formatJob = useRef<object | null>(null);
  const variables = [
    "context.form",
    "context.outputs",
    "context.instance",
    "context.business",
    "context.initiator",
    "context.node",
    "context.operator",
    ...fields.map((field) => `context.form.${field}`),
    ...outputs.map((output) => `context.outputs.${output}`),
  ];
  const variableRef = useRef(variables);
  variableRef.current = variables;
  const check = (text: string) => {
    if (text.length > 16000) return "脚本最多 16000 个字符";
    let position: number | undefined;
    javascriptLanguage.parser.parse(text).iterate({
      enter(node) {
        if (node.type.isError && position === undefined) position = node.from;
      },
    });
    return position === undefined
      ? ""
      : `第 ${text.slice(0, position).split("\n").length} 行附近有语法错误`;
  };
  useEffect(() => {
    const completion = (context: CompletionContext) => {
      const word = context.matchBefore(/[\w.\[\]]*/);
      if (!word || (word.from === word.to && !context.explicit)) return null;
      return {
        from: word.from,
        options: variableRef.current.map((label) => ({
          label,
          type: "variable",
        })),
        validFor: /^[\w.\[\]]*$/,
      };
    };
    const view = new EditorView({
      parent: host.current!,
      doc: value,
      extensions: [
        basicSetup,
        javascript(),
        javascriptLanguage.data.of({ autocomplete: completion }),
        EditorView.contentAttributes.of({
          "aria-label": "JavaScript",
          spellcheck: "false",
        }),
        EditorView.updateListener.of((update) => {
          if (!update.docChanged) return;
          revision.current++;
          setNotice("");
          const source = update.state.doc.toString();
          setError(check(source));
          callback.current(source);
        }),
      ],
    });
    editor.current = view;
    setFormatting(false);
    setError(check(value));
    return () => {
      view.destroy();
      editor.current = null;
      formatJob.current = null;
    };
  }, []);
  useEffect(() => {
    const view = editor.current;
    if (view && view.state.doc.toString() !== value)
      view.dispatch({
        changes: { from: 0, to: view.state.doc.length, insert: value },
      });
  }, [value]);
  function insert(variable: string) {
    if (!variable || !editor.current) return;
    editor.current.dispatch(editor.current.state.replaceSelection(variable));
    editor.current.focus();
  }
  async function format() {
    const owner = editor.current;
    if (!owner || formatJob.current) return;
    const source = owner.state.doc.toString();
    const sourceRevision = revision.current;
    const job = {};
    formatJob.current = job;
    setFormatting(true);
    setNotice("");
    try {
      const [prettier, babel, estree] = await Promise.all([
        import("prettier/standalone"),
        import("prettier/plugins/babel"),
        import("prettier/plugins/estree"),
      ]);
      const formatted = await prettier.format(source, {
        parser: "babel",
        plugins: [babel.default, estree.default],
      });
      if (editor.current !== owner || formatJob.current !== job) return;
      if (revision.current !== sourceRevision) {
        setNotice("代码已变化，未应用旧的格式化结果，请重新格式化。");
        return;
      }
      if (formatted !== source)
        owner.dispatch({
          changes: { from: 0, to: owner.state.doc.length, insert: formatted },
        });
      setError(check(formatted));
      setNotice("代码格式化完成");
    } catch (cause) {
      if (
        editor.current === owner &&
        formatJob.current === job &&
        revision.current === sourceRevision
      )
        setError(cause);
    } finally {
      if (formatJob.current === job) {
        formatJob.current = null;
        if (editor.current === owner) setFormatting(false);
      }
    }
  }
  return (
    <div className="ar-stack">
      <div className="ar-row">
        <Select
          aria-label="插入上下文"
          value=""
          placeholder="插入上下文变量"
          options={variables.map((variable) => ({
            value: variable,
            label: variable,
          }))}
          onValueChange={(nextValue) => insert(nextValue)}
        />
        <Button busy={formatting} onClick={() => void format()}>
          格式化代码
        </Button>
      </div>
      <div ref={host} className="ar-code-editor" />
      <ErrorState error={error} />
      {notice && (
        <Alert>
          <AlertDescription role="status">{notice}</AlertDescription>
        </Alert>
      )}
      <small>
        execute(context) 返回 JSON 对象。真实执行和受限环境校验由后端完成。
      </small>
    </div>
  );
}
