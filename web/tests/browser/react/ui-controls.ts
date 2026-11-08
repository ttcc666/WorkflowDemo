import { expect, type Locator } from "@playwright/test";

export async function chooseOption(trigger: Locator, value: string) {
  await trigger.click();
  const option = trigger
    .page()
    .locator(
      '[role="option"][data-option-value=' + JSON.stringify(value) + "]",
    );
  await expect(option).toBeVisible();
  await option.click();
  await expect(trigger).toHaveAttribute("data-value", value);
}
