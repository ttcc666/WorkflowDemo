import { strict as assert } from "node:assert";
import { test } from "node:test";
import { nativeWidgetFindings } from "./react-ui-audit.mjs";

test("rejects native widgets and native date inputs in consumer JSX", () => {
  const result = nativeWidgetFindings(
    `export default () => <><button>Run</button><details><summary>Info</summary></details><Input type='date' /><Input type={'date'} /><datalist><option /></datalist></>`,
  );
  assert.deepEqual(
    result.map((finding) => finding.tag),
    [
      "button",
      "details",
      "summary",
      "native date Input",
      "native date Input",
      "datalist",
      "option",
    ],
  );
});
test("allows official component compositions and semantic content", () => {
  assert.deepEqual(
    nativeWidgetFindings(
      "export default () => <Card><h1>Title</h1><Select/><DatePicker/><Accordion><AccordionItem><AccordionTrigger>Details</AccordionTrigger><AccordionContent><pre>JSON</pre></AccordionContent></AccordionItem></Accordion></Card>",
    ),
    [],
  );
});
test("does not treat JSON strings or comments as rendered native widgets", () => {
  assert.deepEqual(
    nativeWidgetFindings(
      'const fixture = "<button>example</button>"; /* <input /> */ export default () => <Input />',
    ),
    [],
  );
});
