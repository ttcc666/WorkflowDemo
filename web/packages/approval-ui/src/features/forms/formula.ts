// 计算字段公式（与 Approval.Core.Forms.Formula 同一语法与结果，共用 tests/shared/formula-cases.json 测试用例）：
// 数字、字段标识（不区分大小写）、+ - * / ()、比较（> < >= <= == !=，只能用作 if 的条件），
// 函数 round(x, n)、min(…)、max(…)、abs(x)、if(条件, 是, 否)、ifnull(x, 默认值)。除以 0、空值参与运算（ifnull 除外）结果为空。

export const FORMULA_MAX_LENGTH = 500
const MAX_DEPTH = 32

type ValueType = 'number' | 'boolean'
type Value = number | boolean | null
type Variables = (name: string) => number | null | undefined

interface Node {
  type: ValueType
  /** 数字字面量的值（round 的位数必须是字面量） */
  literal?: number
  evaluate(vars: Variables): Value
}

export interface ParsedFormula {
  text: string
  /** 引用的字段标识（原样，比较时不区分大小写） */
  identifiers: string[]
  /** 计算结果；除以 0、空值参与运算时为 null */
  evaluate(vars: Variables): number | null
}

export type ParseResult = { formula: ParsedFormula; error?: undefined } | { formula?: undefined; error: string }

class FormulaSyntaxError extends Error {}

/**
 * 消除二进制浮点误差（如 0.1 + 0.2、4.35 * 100）：每步运算后保留 15 位有效数字，
 * 使结果与服务端 decimal 计算一致。
 */
function clean(value: number): number | null {
  if (!Number.isFinite(value)) return null
  const result = Number(value.toPrecision(15))
  return result === 0 ? 0 : result
}

/** 四舍五入（远离零），与服务端 MidpointRounding.AwayFromZero 一致 */
export function roundTo(value: number, precision: number): number {
  const factor = 10 ** precision
  const shifted = Number((Math.abs(value) * factor).toPrecision(15))
  const result = (Math.sign(value) * Math.round(shifted)) / factor
  return result === 0 ? 0 : Number(result.toPrecision(15))
}

// ───── 词法 ─────

type TokenKind = 'number' | 'identifier' | 'operator' | '(' | ')' | ',' | 'end'

interface Token {
  kind: TokenKind
  text: string
  position: number
}

const isDigit = (c: string | undefined) => c !== undefined && c >= '0' && c <= '9'
const isIdentStart = (c: string | undefined) => c !== undefined && /[A-Za-z_]/.test(c)
const isIdentPart = (c: string | undefined) => c !== undefined && /[A-Za-z0-9_]/.test(c)

function tokenize(text: string): Token[] {
  const tokens: Token[] = []
  let i = 0
  while (i < text.length) {
    const c = text[i]!
    if (/\s/.test(c)) {
      i++
      continue
    }
    const start = i
    if (isDigit(c) || (c === '.' && isDigit(text[i + 1]))) {
      while (isDigit(text[i])) i++
      if (text[i] === '.') {
        i++
        if (!isDigit(text[i])) throw new FormulaSyntaxError(`第 ${start + 1} 个字符处的数字格式不正确`)
        while (isDigit(text[i])) i++
      }
      tokens.push({ kind: 'number', text: text.slice(start, i), position: start })
    } else if (isIdentStart(c)) {
      while (isIdentPart(text[i])) i++
      tokens.push({ kind: 'identifier', text: text.slice(start, i), position: start })
    } else if (['>=', '<=', '==', '!='].includes(text.slice(i, i + 2))) {
      tokens.push({ kind: 'operator', text: text.slice(i, i + 2), position: start })
      i += 2
    } else if ('+-*/<>'.includes(c)) {
      tokens.push({ kind: 'operator', text: c, position: start })
      i++
    } else if (c === '(' || c === ')' || c === ',') {
      tokens.push({ kind: c, text: c, position: start })
      i++
    } else {
      throw new FormulaSyntaxError(`第 ${start + 1} 个字符 '${c}' 无法识别`)
    }
  }
  tokens.push({ kind: 'end', text: '', position: text.length })
  return tokens
}

// ───── 语法：递归下降 ─────

const comparisons = new Set(['>', '<', '>=', '<=', '==', '!='])

function asNumber(value: Value) {
  return typeof value === 'number' ? value : null
}

function binary(op: string, left: Node, right: Node): Node {
  const arithmetic = '+-*/'.includes(op)
  return {
    type: arithmetic ? 'number' : 'boolean',
    evaluate(vars) {
      const a = asNumber(left.evaluate(vars))
      const b = asNumber(right.evaluate(vars))
      if (a === null || b === null) return null
      switch (op) {
        case '+':
          return clean(a + b)
        case '-':
          return clean(a - b)
        case '*':
          return clean(a * b)
        case '/':
          return b === 0 ? null : clean(a / b)
        case '>':
          return a > b
        case '<':
          return a < b
        case '>=':
          return a >= b
        case '<=':
          return a <= b
        case '==':
          return a === b
        default:
          return a !== b
      }
    },
  }
}

class Parser {
  private index = 0
  private depth = 0
  readonly identifiers = new Map<string, string>()

  constructor(private readonly tokens: Token[]) {}

  private get current() {
    return this.tokens[this.index]!
  }

  private next() {
    return this.tokens[this.index++]!
  }

  parseFormula(): Node {
    const node = this.parseComparison()
    if (this.current.kind !== 'end') throw this.unexpected()
    return node
  }

  private parseComparison(): Node {
    const left = this.parseAdditive()
    if (this.current.kind !== 'operator' || !comparisons.has(this.current.text)) return left
    const op = this.next().text
    const right = this.parseAdditive()
    if (this.current.kind === 'operator' && comparisons.has(this.current.text)) throw new FormulaSyntaxError('比较运算不能连写，请用 if 嵌套')
    return binary(op, requireNumber(left), requireNumber(right))
  }

  private parseAdditive(): Node {
    let node = this.parseTerm()
    while (this.current.kind === 'operator' && (this.current.text === '+' || this.current.text === '-')) {
      const op = this.next().text
      node = binary(op, requireNumber(node), requireNumber(this.parseTerm()))
    }
    return node
  }

  private parseTerm(): Node {
    let node = this.parseUnary()
    while (this.current.kind === 'operator' && (this.current.text === '*' || this.current.text === '/')) {
      const op = this.next().text
      node = binary(op, requireNumber(node), requireNumber(this.parseUnary()))
    }
    return node
  }

  private parseUnary(): Node {
    if (this.current.kind === 'operator' && (this.current.text === '-' || this.current.text === '+')) {
      const negate = this.next().text === '-'
      const operand = requireNumber(this.nested(() => this.parseUnary()))
      if (!negate) return operand
      return {
        type: 'number',
        evaluate: (vars) => {
          const value = asNumber(operand.evaluate(vars))
          return value === null ? null : clean(-value)
        },
      }
    }
    return this.parsePrimary()
  }

  private parsePrimary(): Node {
    const token = this.current
    if (token.kind === 'number') {
      this.next()
      const value = Number(token.text)
      return { type: 'number', literal: value, evaluate: () => value }
    }
    if (token.kind === 'identifier' && this.tokens[this.index + 1]?.kind === '(') return this.parseFunction()
    if (token.kind === 'identifier') {
      this.next()
      if (!this.identifiers.has(token.text.toLowerCase())) this.identifiers.set(token.text.toLowerCase(), token.text)
      return {
        type: 'number',
        evaluate: (vars) => {
          const value = vars(token.text)
          return typeof value === 'number' && Number.isFinite(value) ? value : null
        },
      }
    }
    if (token.kind === '(') {
      this.next()
      const node = this.nested(() => this.parseComparison())
      this.expect(')', '缺少右括号')
      return node
    }
    throw this.unexpected()
  }

  private parseFunction(): Node {
    const nameToken = this.next()
    const name = nameToken.text.toLowerCase()
    this.next() // (
    const args: Node[] = []
    if (this.current.kind !== ')') {
      args.push(this.nested(() => this.parseComparison()))
      while (this.current.kind === ',') {
        this.next()
        args.push(this.nested(() => this.parseComparison()))
      }
    }
    this.expect(')', `函数 ${nameToken.text} 缺少右括号`)

    const arity = (min: number, max = min) => {
      if (args.length < min || args.length > max)
        throw new FormulaSyntaxError(min === max ? `函数 ${name} 需要 ${min} 个参数` : `函数 ${name} 至少需要 ${min} 个参数`)
    }

    switch (name) {
      case 'if': {
        arity(3)
        if (args[0]!.type !== 'boolean') throw new FormulaSyntaxError('if 的第一个参数必须是比较，如 if(amount > 1000, 1, 0)')
        const [condition, yes, no] = [args[0]!, requireNumber(args[1]!), requireNumber(args[2]!)]
        return {
          type: 'number',
          evaluate: (vars) => {
            const value = condition.evaluate(vars)
            return typeof value === 'boolean' ? (value ? yes : no).evaluate(vars) : null
          },
        }
      }
      case 'ifnull': {
        arity(2)
        const [value, fallback] = [requireNumber(args[0]!), requireNumber(args[1]!)]
        return {
          type: 'number',
          evaluate: (vars) => {
            const result = asNumber(value.evaluate(vars))
            return result !== null ? result : fallback.evaluate(vars)
          },
        }
      }
      case 'round': {
        arity(2)
        const value = requireNumber(args[0]!)
        // 第二个参数必须是 0–10 的整数字面量
        const digits = args[1]!.literal
        if (digits === undefined || !Number.isInteger(digits) || digits > 10)
          throw new FormulaSyntaxError('round 的第二个参数必须是 0–10 的整数，如 round(amount, 2)')
        return {
          type: 'number',
          evaluate: (vars) => {
            const result = asNumber(value.evaluate(vars))
            return result === null ? null : roundTo(result, digits)
          },
        }
      }
      case 'abs': {
        arity(1)
        const value = requireNumber(args[0]!)
        return {
          type: 'number',
          evaluate: (vars) => {
            const result = asNumber(value.evaluate(vars))
            return result === null ? null : Math.abs(result)
          },
        }
      }
      case 'min':
      case 'max': {
        arity(1, Number.MAX_SAFE_INTEGER)
        const values = args.map(requireNumber)
        return {
          type: 'number',
          evaluate: (vars) => {
            const results = values.map((v) => asNumber(v.evaluate(vars)))
            if (results.some((r) => r === null)) return null
            return name === 'min' ? Math.min(...(results as number[])) : Math.max(...(results as number[]))
          },
        }
      }
      default:
        throw new FormulaSyntaxError(`不支持的函数 ${nameToken.text}（可用：round、min、max、abs、if、ifnull）`)
    }
  }

  private nested<T>(parse: () => T): T {
    if (++this.depth > MAX_DEPTH) throw new FormulaSyntaxError('公式嵌套层数过多')
    try {
      return parse()
    } finally {
      this.depth--
    }
  }

  private expect(kind: TokenKind, message: string) {
    if (this.current.kind !== kind) throw new FormulaSyntaxError(message)
    this.next()
  }

  private unexpected() {
    return this.current.kind === 'end'
      ? new FormulaSyntaxError('公式不完整')
      : new FormulaSyntaxError(`第 ${this.current.position + 1} 个字符处的 '${this.current.text}' 不符合语法`)
  }
}

function requireNumber(node: Node): Node {
  if (node.type !== 'number') throw new FormulaSyntaxError('比较的结果只能用作 if 的条件')
  return node
}

export function parseFormula(text: string | null | undefined): ParseResult {
  if (!text?.trim()) return { error: '公式为空' }
  if (text.length > FORMULA_MAX_LENGTH) return { error: `公式不能超过 ${FORMULA_MAX_LENGTH} 个字符` }
  try {
    const parser = new Parser(tokenize(text))
    const root = parser.parseFormula()
    if (root.type !== 'number') return { error: '公式的结果必须是数值（比较只能用作 if 的条件）' }
    return {
      formula: {
        text,
        identifiers: [...parser.identifiers.values()],
        evaluate: (vars) => asNumber(root.evaluate(vars)),
      },
    }
  } catch (e) {
    if (e instanceof FormulaSyntaxError) return { error: e.message }
    throw e
  }
}
