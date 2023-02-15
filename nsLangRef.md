# NusantaraScript Language Reference

NusantaraScript (NS) is a subset of [Šimi - the Warp edition](https://github.com/globulus/simi/), with some functionality kicked out to make the language simpler. It's also fully rewritten in C# to allow usage within [Nusantara's Unity engine](https://playnusantara.com).

## Basic syntax
For the most part, NS is a usual curly-brace language.

### Keywords
NS has 26 reserved keywords:
```ruby
and break class continue fn do else false fib for gu if import in 
is ivic native nil not or print return self super true when while yield
```

### Comments
```ruby
# This is a single line comment
a = 3 # They span until the end of the line

#* And here is
a multiline comment
spanning multple lines *#
```

> *Design note* - why use # for comment start instead of //? 1. It's one character as opposed to two. 2. I wanted to leave // for integer division.

### Identifiers
Identifiers start with a letter, or _, and then may contain any combination of letters, numbers, or underscores. Identifiers are case-sensitive.
```ruby
# Here are some valid identifiers
a
a123
_abc
___abc123_345a__b54___
```

### Newlines
Line breaks separate NS statements and as such are meaningful. E.g, an assignment statement must be terminated with a newline. The compiler will usually warn you if a newline is missing.

If your line is very long, you can break it into multiple lines by ending each line with a backslash *\\* for readability purposes.
```ruby
a = 5 # This is a line
# Here are two lines, the first one nicely formatted by using \
arr = [1, 2, 3]\
  .reversed()\
  .joined(with: [5, 6, 7])\
  .where(=> $0 <= 5)\
  .map(=> $0 * 2)\
  .sorted(fn (l, r) => r.compareTo(l))
```

On the other hand, if you wish to pack multiple short statements onto a single line, you can separate them with a semicolon *;*. To the lexer, a newline and a semicolon are the same token.
```ruby
a = 3; b = 5 # And here are two assignment statements separated by ;
```

### Code blocks
A block of code starts with a left brace *{*, followed by one or multiple statements, terminated with a right brace *}*. This allows the blocks to span an arbitrary number of lines and always have a clearly designated start and end.
```ruby
if a < 5 { b = 3 } # Single line block
else { # Multiline block
  a += 3
  b = 4
}
```

### Variables
Declare a variable and assign a value to it with the *=* operator:
```ruby
a = 5
b = "string"
c = true
a = 6
```

#### Constants
Some variables are real constants and using *=* for a reassignment on them will result in a compiler error. These are:
1. Variables whose name is in CAPS_SNAKE_CASE.
2. Declared classes and functions, regardless of their case.
3. Variables declared with *_=* instead of *=*. The *_=* operator was introduced primarily to allow the SINGLETON OBJECTS LINK to be declared as constants.
```ruby
THIS_IS_A_CONST = 5
THIS_IS_A_CONST = "error"

fn func() {
  return 2
}
func = "this is also an error"

realConst _= 10
realConst = "again, a compile time error"
```

### Scoping
NS uses lexical scoping, i.e a variable is valid in the block it was declared in and its nested blocks:
```ruby
a = 5
{
    a += 3 # Works, a is visible in a nested block
    b = 6
    b += 4 # Yup, b is visible here as well
}
a += 1 # Works
b += 1 # Compile error, b is not declared in this scope
```

**Name shadowing is prohibited and results in a compile error.** Vast majority of the time, there's no good reason to shadow a variable from a parent scope, and it makes the code more difficult to read and more prone to errors.
```ruby
a = 5
{
  a = 6 # Compile error, a was already declared in an outer block
}
```

## Primitive values
This guide provides an overview of basic, value-based NS types: integers, floats, booleans and strings.

### Null
*null* is a singleton that represents an absence of value. In a way, *null* always points to itself - any operation on it also results in null.

### Booleans
Boolean values can take only two values: *true* or *false*. The most important usage of the boolean type is that, in NS, **false is the only *false* value - everything else is true**. Logical, equality and comparison operators all return booleans. Conditional statements all require booleans to operate.

Booleans can't interoperate with numbers, like they can in some languages.
```ruby
a = true
b = false
c = a || b # c is true
if a { ... }
else if b { ... }
else {
  d = a + 20 # This is a runtime error, luckily this branch won't be reached
}
```

### Integers
Integer data type (wrapper class `Int`) represents 64-bit integer numerical values:

```ruby
a = 5
b = 123249234
c = 100_000_000
```

### Floating-point values
Float data type (wrapper class `Float`) represents 64-bit floating-point numerical values:

```ruby
a = 1.2
b = 324234.32423432
c = 1.345345e-10
```

### Strings
Strings are arrays of characters, enclosed in double quotes. You can freely put any characters inside a string, including newlines. Regular escape characters can be included.
```ruby
string = "this is a
    multiline
 string with tabs"
anotherString = "this is another 'string'"
```

String interpolation is supported by enclosing the nested expressions into *$(SOMETHING)*. Single, non-keyword identifiers don't need the parentheses, they can just be preceeded by a *$*:

```ruby
a = 2
b = 3
Console.print("a is $a, and doubled it is $(a * 2) and b, which is $b, minus 1 halved is $((b - 1) / 2)")
# Prints: a is 2, doubled is 4 and b, which is 3, minus 1 halved is 1
```

When used as objects, strings are boxed into class *String*, which contains useful methods for string manipulation. Since strings are immutable, all of these methods return a new string.

Strings are pass-by-value, and boxed strings are pass-by-reference.

The String class contains a native *builder()* method that allows you to concatenate a large number of string components without sacrificing the performance that results from copying a lot of strings:
```ruby
class Range {

    ... rest of implementation omitted ...

    fn toString = String.builder()\
            .add("Range from ").add(@start)\
            .add(" to ").add(@stop)\
            .add(" by ").add(@step)\
            .build()
}
```

### Boxed primitive values
Boxed primitive values are objects with two fields, a class being Num or String, respectively, and a private field "_" that represents the raw value. The raw value can only be accessed by methods and functions that extend the Stdlib classes, and is read-only. Using the raw value alongside the @ operator results in the so-called *snail* lexeme:
```ruby
# Implementation of times() method from Number class
fn times = ife(@_ < 0, =Range(@_, 0), =Range(0, @_)).iterate()
```

## Operators

### Addition
\+

* When both operands are numbers, it performs addition.
* When either operand is a String, the non-String operand is *stringified* and the two strings are concatenated.
* When the left-hand operand is a mutable List, the right-hand operand is appended to that List.

### Arithmetic
-, *, /, //, %

* Only work on numbers.
* \- Can be used as an unary operator.
* // is the integer division operator, 3 // 2 == 1, while 3 / 2 == 1.5.

### Assignment and compound assignment
=, _=, +=, -=, *=, /=, //=, %=, ??=

### Logical
!, &&, ||

* *!* is unary, *&&* and *||* are binary.
* *&&* and *||* are short-circuit operators (and-then and or-else).

### Equality
==, !=

The equality operator checks if two values are, well, equal. The thing is, it's overloadable to a degree:
* If the provided values are in fact the same, i.e the same number, same string or the reference to the same function/object/class/whatever, it returns true.
* If this isn't the case and provided values are objects, it looks up if the said object (or one of its superclasses) overloads the *equals()* method. If so, it invokes the method and returns its result. This allows for implementation of custom definitions of equality based on object types - e.g, two Ranges are equal if their start and stop values are the same:
```ruby
class Range {
    # ... rest of implementation omitted ...

    fn equals(other) = start == other.start and stop == other.stop
}
```

Again, you can't override the default behaviour and say that two references to the same thing aren't equal, because that would: a. be silly, b. it would force you to handle that use-case manually, which means boilerplate code.

The operator that checks for inequality is *!=*. Since *THIS != THAT* is just *not (THIS == THAT)*, overloading *equals* affects *!=* as well. 

### Comparison
<, <=, >, >=

The usual stuff - less than, less or equal, greater than, greater or equal. All of those operate on numbers only.

### Range operators
*Range* is an oft-used Core class, so it has its own dedicated set of operators that operate on numbers.

* *..* (two dots) creates a range from left hand side *until* right hand side - up to, but not including:
```ruby
1..10 # 1, 2, 3, 4, 5, 6, 7, 8, 9
```
* *...* (three dots) creates a range from left hand side *to* right hand side - up to, and including:
```ruby
1...10 # 1, 2, 3, 4, 5, 6, 7, 8, 9, 10
```

### is and !is
You can check if a value is of a certain type with the *is* operator. Its inverted version is *!is*.

The right hand side must be a class. Since everything in NS is an object (or can be boxed into one), you can dynamically check the type of anything on the left hand side:
* *null is Anything* always returns false.
* For integers: var is Int
* For floats: var is Float
* For Strings: var is String
* For functions: var is Function
* For classes: var is Class
* For objects: var is Object
* For lists: var is List
* To check is an object is an instance of a class or any of its subclasses: var is SomeClass.
* You can also use *is* to see if a class is a subclass of another class: SomeClass is PotentialSuperClass.
    * > *Design note:* This part is debatable, as *is* is supposed to check *what type a value is*, but then again, it flows well with the *is* keyword for subclassing. It might be removed in the future, or replaced with *>* (although that one isn't without issues either).

```ruby
a = [1, 2, 3, 4]
a is Object # true
a is not Int # true
b = 5
b is Int # true
b is String # false
car = Car("Audi", "A6", 2016)
car is Car # true
car is not Object # false

TODO expand
```

### in and !in
The *in* operator implicitly calls the *has()* method. This means that *in* is a fully overloadable operator, just be mindful it's operands are inverted: THIS in THAT is equal to THAT.has(THIS). It's negated variant is *not in*. Since *THIS not in THAT* is just *not (THIS in THAT)*, overloading *has* affects *not in* as well. 
 
 Method *has()* is defined for Objects, Lists and Strings, but not for Numbers.
 * For Objects, it checks if the provided value is the object key.
 * For Lists, it checks if the value is in the list.
 * For strings, it checks if the provided value, which must be a String, is a substring.

Again, this method can be overriden in any class. See, for example, how it's done in the Range class:
```ruby
class Range {
    # ... rest of implementation omitted ...

    fn has(val) = if start < stop {
            val >= start and val < stop
        } else {
            val <= start and val > stop
        }
    }
}
"str" in "substring" # true
"a" not in "bcdf" # true
2 in [1, 2, 3] # true
"a" in [b = 2, c = 3] # false
range = Range(1, 10)
2 in range # true
10 not in range # true
```

### ?? - nil coalescence
The *??* operator checks if the value for left is nil. If it is, it returns the right value, otherwise it returns the left value. This operator is short-circuit (right won't be evaluated if left is not nil).
```ruby
a = b ?? c # is equivalent to a = if b != nil b else c, just faster
```
You can use *??=* with variables to assign a value to it only if its current value is nil:
```ruby
a = nil
a ??= 5 # a is 5
b = 3
b ??= 5 # b is 3
```

### @ - self referencing
*@* isn't a real operator - it maps exactly to *this.*, i.e *@tank* is identical to writing *this.tank*. It's primarily there to save time and effort when implementing classes (when you really write a lot of *this.* s).

### Precedence and associativity
| Precedence | Operators                   | Description                                        | Associativity |
|------------|-----------------------------|----------------------------------------------------|---------------|
| 1          | . ?. () ?()                 | (safe) get, (safe) call                            | left          |
| 2          | ! -                         | invert, negate                                     | right         |
| 3          | ??                          | nil coalescence                                    | left          |
| 4          | / // * %                    | division, integer division, modulo, multiplication | left          |
| 5          | + -                         | addition, subtraction                              | left          |
| 6          | .. ...                      | range until, range to                              | left          |
| 7          | < <= > >= <>                | comparison, matching                               | left          |
| 8          | == != is !is in !in         | (in)equality, type tests, inclusion test           | left          |
| 9          | &&                          | logical and                                        | left          |
| 10         | ||                          | logical or                                         | left          |
| 11         | = _= += -= *= /= //= %= ??= | assignment                                         | right         |
| 12         | ?!                          | rescue                                             | left          |


## Control flow

### Truth
In NS, only boolean values can represent truth. There's no implicit conversion of numerical or string values into booleans.

### if-else if-else
Not much to say here, NS offers the usual if-else if-...-else -else structure for code branching.
```ruby
if a < b {
  c = d
  print c
} else if a < c {
 print "a < c"
} else if a < d {
  a = e
  print e
} else {
 print f
}
```

The parentheses around conditions are optional, and so are the braces around the post-condition statement, if there's only one of it (multiple statements require a block, of course).

### when
If you're comparing a lot of branches against the same value, it's more convenient to use a *when* statement.
* It supports *is*, *is not*, *in* and *not in* operators; otherwise it assumes that the operator is *==*.
* *if* can be used as an operator to denote that its right-hand side should be evaluated as-is (useful for function calls).
* You can use the *||* operator to check against multiple conditions in a single branch.
* *when* statement can be made exhaustive by adding an *else* block at the end.
```ruby
when a {
  5 {
    print "a is 5"
  }
  10 || 13 || 15 {
    print "a is 10 or 13 or 15"
  }
  is String {
    print "a is String"
  }
  not in 12...16 {
    print "not between 12 and 16"
  }
  if isValidString(a) || is Int {
    print "is valid string or a number"
  }
  else {
    print "reached the default branch"
  }
}
```

Note that *when* is a syntax sugar, i.e it compiles the same way as if-else if-else blocks would.

### *if* and *when* expressions
*if-else if-else* branches (and, by default, its alternate form of *when*) can be used as expressions. The last line in the block (or the only value if it's a block-less statement) must be an expression, or a return/break/continue - otherwise, a compiler will report an error.
```ruby
a = if someCondition {
  3
} else if otherConditions {
  print "something"
  return "I'm outta this function"
} else if yetMoreConditions {
  print "doing this"
  doThis()
} else {
  2 + 2 * 3
}
```

The *when* equivalent different only as it allows the use of *=>* for the single-line expression block:
```ruby
a = when b {
  3 || 4 => 10 # See how easy and fast this is to type?
  6 || 7 => 20
  is String => 30
  is List {
    d = a * 3 + 15
    d + 20 # The last line in the block must still be an expression
  }
  else => 50
}
```

Naturally, functions with an implicit return of their single expression work with *if* and *when* expressions as well:
```ruby
fn extractValue(obj) => when obj {
  is A => obj.aValue()
  is B || is C => obj.bOrCValue()
  else => obj.otherValue()
}
```

Let's rewrite the *when* example from the previous section as an expression:
```ruby
print when a {
    5 = "a is 5"
    10 or 13 or 15 = "a is 10 or 13 or 15"
    is String = "a is String"
    not in Range(12, 16) = print "not between 12 and 16"
    if isValidString(a) or is Number = "is valid string or a number"
    else = "reached the default branch"
}
```

### while loop
The *while* block is executed as long as its condition is true:
```ruby
while a < 10 {
  Console.print(a)
  a *= 2
}
```

### do-while loop
The *do-while* loop also executes while its condition is true, but its condition is checked at the end, meaning its body is always going to execute at least once.
```ruby
a = 10
do {
  Console.print(a) # This will still get printed because condition is evaluated at the end
  a *= 2
} while a < 3
```

### for-in loop
NS offers a *for-in* loop for looping over iterables (and virtually everything in NS is iterable). The first parameter is the value alias, while the second one is an expression to iterate over. The block of the loop will be executed as long as the iterator supplies non-nil values (see section below).
```ruby
for i in 6.times() {  # Prints 0 1 2 3 4 5
 Console.log(i)
}
for c in "abcdef" { # Prints a b c d e f
 Console.log(c)
}

# Iterating over a list iterates over its values
for value in [10, 20, 30, 40] { # Prints 10 20 30 40
 Console.log(value)
}

object = [a: 1, b: "str", c: Pen(color: "blue")]
# Iterating over keyed objects over its keys
for key in object {
 Console.log(key) # Prints a, b, c
}
# Object decomposition syntax can be used with for loop as well
for [key, value] in object.zip() {
 print key + " = " + value # Prints a = 1, b = str, etc.
}
```

If the expression is *null*, the loop won't run. However, if it isn't nil and doesn't resolve to an iterator, a runtime error will be thrown.

### Iterables and iterators
The contract for these two interfaces is very simple:
* An *iterable* is an object that has the method *iterate()*, which returns an iterator. By convention, every invocation of that method should produce a new iterator, pointing at the start of the iterable object.
* An *iterator* is an object that has the method *next()*, which returns either a value or nil. If the iterator returns nil, it is considered to have reached its end and that it doesn't other elements.

Virtually everything in NS is iterable:
1. The Number class has methods times(), to() and downto(), which return Ranges (which are iterable).
2. The String class exposes a native iterator which goes over the characters of the string.
3. The Object class exposes a native iterator that works returns values for arrays, and keys for objects. There's also a native zip() method, that returns an array of \[key = ..., value = ...] objects for each key and value in the object.
4. Classes may choose to implement their own iterators, as can be seen in the Stdlib Range class.

### break and continue
The *break* and *continue* keywords work as in other languages - break terminates the active loop, while continue jumps to its top. They must be placed in loops, otherwise you'll get a compile error.

### return and yield
Control flow in functions happens via the *return* statement.

The *return* statement behaves as it does in other languages - the control immediately returns to whence the function was called, and can either pass or not pass a value:
 ```ruby
 fn f(x) {
  if x < 5 {
    return 0
  }
  return x
 }
 Console.log(f(2)) # 0
 Console.log(f(6)) # 6
 ```

 A *return* always returns from the current function, be it a lambda or not. Some languages make a distinction there, but NS does not.


## Functions

Functions are callable blocks of code. Functions can have arguments passed to them, and can return values to their call site, just as in other languages.

### Function anatomy
Define a function with the **fn** keyword, followed by a name, argument list and the body:
```ruby
fn function(arg1, arg2, arg3) {
  c = arg2 + arg3
  return c
}
```

The arguments are optional, and the argument list can be omitted if it's empty:
```ruby
fn arglessFunc {
  Console.print("doing something")
}
```

The body is optional as well:
```ruby
fn iAintDoingMuchRightNow(a, b, c)
```

### Calling functions
The basic function operation is a *call* - invoking the function with its designated arguments so that its body executes and returns a value. A call always involves a pair of parentheses, even if the function doesn't receive any arguments.
```ruby
fn func(a, b) {
  return a + b
}
Console.log(func(2, 3)) # prints 5
```

NS function are first-class citizens of the language, meaning they can be stored in variables and passed around as arguments, just like any other values:
```ruby
fn function(a, b, c) { ... }
var1 = function
var1(1, 2, 3)

fn secondOrderFn(otherFn) {
  print otherFn(1, 2, 3)
}
secondOrderFn(function)
```

When calling functions, you can pass names to parameters to improve readability. These take form of *identifier: value*, but don't affect the call in any way, as they're just ignored by the compiler:
```ruby
function(a: 3, b: 5, c: 4) # Just adds some readability
```

### Returning values
A **return** statement aborts function execution and jumps back to its call site. A return statement may or may not return a value - if no expression is specified after the *return* keyword, the default value is *null*. Also, all functions have an implicit *return null* at their bottom:
```ruby
fn implicitlyReturnsNull() {
  Console.log("a")
}

fn explicitReturnWithImplicitNuul() {
  for item in collection {
    if item.isBad() {
      return # nil is implicit
    }
  }
}

fn explicitReturn(name) {
  return "My name is: \(name)."
}
```

### Default argument values
Function arguments can have default values. It also implicitly overloads the function by arity. All the arguments with default values must come after all the arguments without default values:
```ruby
fn fnWithDefaultArgs(a, b, c = 13, d = "string") {
    ....
}

fnWithDefaultArgs(1, 2, 3, "d")
fnWithDefaultArgs(1, 2, 3) # equals fnWithDefaultArgs(1, 2, 3, "string)
fnWithDefaultArgs(1, 2) # equals fnWithDefaultArgs(1, 2, 3, "string")
fnWithDefaultArgs(1) # An error, two args are required
```

Presently, only *raw value literals* (numbers, strings and booleans) can be passed as default argument values.

### Expression functions
Functions whose body boils down to a single return can use the *expression function* syntax sugar - just place *=>* instead of the body, and omit the return:
```ruby
fn expressionFn(a, b) => a + b # is the same as { return a + b }
```

Such functions are quite common, both as regular functions, methods or lambdas.

### Lambdas
Function creation can be an expression. This allows you to create anonymous functions (lambdas). The syntax is the same as with regular function, just omitting the name:
```ruby
secondOrderFunction(fn (a, b, c) { return a + b + c})

fnStoredInVar = fn (a, b) {
  Console.log(a + b)
}
fnStoredInVar(2, 3)
```

### Implicit arguments for lambdas
A common use-case for lambdas, especially when passed as arguments to higher-order functions, is to return a value based on a set of well-established arguments. Consider the List *where* method, which filters list values based on a predicate:
```ruby
filteredList = list.where(fn (value) => value > 5)
```

Since everyone knows what does the *where* function expect, you can type this even quicker with *implicit arguments*:
```ruby
filteredList = list.where(=> $0 > 5)
```

You can omit the *fn* keyword and the argument list and jump straight to the equals sign that indicates a *return*. Implicit arguments start with a dolar sign and are sequentially numbered starting from 0: *$0, $1, $2, ...*. The compiler looks the lambda body up and discovers these implicit arguments, checks if they're properly ordered and synthesizes them for you. The number of allowed implicit arguments isn't limited.

Implicit arguments aren't just for expression lambdas, regular lambdas can use them as well:
```ruby
print fn {
  return $0 + $1
}(1, 2) # prints 3
```

Just remember that improvements to conciseness shouldn't be made at the expense of readability.

### Closures
All NS functions are closures - they have access to variables declared outside their scope and hold onto them. These variables are immutable from within the function.

## Objects and lists
Objects are a centerpiece of NS, its most flexible and powerful construct. Objects are omnipresent - even primitive values get wrapped in objects, lists are objects, and so are all classes and their instances - then again, the one of NS's goals was to make objects simple to understand and work with.

### Object basics and object literals
An object is a sequence of key-value pairs. Keys are strings or identifiers, while a value can be any NS value - number, string, boolean, object, list, etc. Nothing more or less to it - an object contains some data and some functionality, all baked together and accessible via its keys. Each key-value pair constitutes a **field**.

Declare an object with an *object literal* - a set of "key: value" pairs, separated by commas, enclosed in brackets:
```ruby
myFirstObject = [name: "Mike", age: 30, work = fn { Console.print("Yep, working") }]

populations = [tokyo: 37_400_068, delhi: 28_514_000, shanghai: 25_582_000]
```

In essence, the NS notion of objects is most similar to that of JavaScript, which I like because of its simplicity - people intuitively understand maps/hashes/dictionaries, and presenting objects as such structures demystifies the concept.

### Accessing object fields
Access object fields with the dot operator **.**, just as in virtually any other language.
```ruby
Console.print(myFirstObject.name) # Mike
Console.print(myFirstObject.age) # 30
```

If you're passing something other than an identifier, wrap it in parentheses:
```ruby
fieldToAccess = "name"
Console.print(myFirstObject.(fieldToAccess)) # Mike
```

This is called a *getter* - an expression that gets a field from an object.

The **.()** construct is akin to the *subscript operator []* in other languages - any expression within the parentheses will be evaluated and the object will return the corresponding value.

Accessing an invalid key returns **null**:
```ruby
Console.print(myFistObject.invalidKey) # null
```

A getter's counterpart is a *setter*:
```ruby
populations.cairo = 20_076_000 # Added a new field to the mutable object
populations.tokyo = 37_400_069 # Updated an existing field
```

All objects have a *class*. The basic class (which (almost) all other classes inherit) is "Object". Objects created via object literals have "Object" as their class. You can access an object's class through the special **class** field:
```ruby
Console.print(myFirstObject.class) # Object
```

### self and internal modification
Any object can access itself from within itself with the **this** keyword. All object functions can access this, and, more importantly, use it to modify object's fields. This is why we say that NS objects are always mutable from within.
```ruby
obj = [a: 5, fn increment {
  this.a += 1
}]
Console.print(obj.a) # 5
obj.increment()
Console.print(obj.a) # 6
```

Typing **this.** over and over again can be tiresome, so NS allows you to use **@** instead:
```ruby
obj = [a: 5, fn increment {
  @a += 1
}]
```

Using @ instead of *this.* is idiomatic and is used in all code examples. Usage of *this* is limited to instances where it's not followed by a dot (i.e, it's used as an identifier).

### List basics and list literals
You're most likely already familiar with lists from other languages - they are ordered and their values are indexed. In NS, Lists can contain mixed values of any type.
```ruby
list = [100, "abc", 300, => $0 * $1 - $2]
```

Lists are also objects (their base class, "List", inherits the "Object" class). This means that virtually everything that was said earlier about objects works for lists as well.

Access list elements with an index:
```ruby
Console.print(list.0) # 100
Console.print(list.2) # 300
```

If you're supplying a raw integer as the index, the parentheses aren't needed. Otherwise, use the parentheses as the subscript operator:
```ruby
list = [1, 2, 3, 4, 5]
i = 2
Console.print(list.(i)) # 3
```

Negative indices are supported, and refer to the list items starting from the read, with -1 being the last item, -2 the one before the last, etc. Note that, since we're using negative numbers, the getter expression must always be parenthesized.
```ruby
list = [1, 2, 3, 4, 5]
Console.print(list.(-1)) # 5
Console.print(list.(-3)) # 3
```

### Empty object and list literals
Some languages make a distinction between object/map/dictionary and list literals, mostly by enclosing the former in curly braces `{}`, and lists in square brackets `[]`. NS makes use of `[]` for both, primarily because we wanted `{}` to always indicate a block of code. This, however, leaves us with a doubt - what does an empty pair of brackets represent - an empty list or an empty object?

A good cue to visually distinct list and objects is to look for an equal sign `:`. If you see one, you're looking at an object. Therefore, `[]` is an empty list, while `[:]` is an empty object:
```ruby
emptyList = []
emptyObject = [:]
```

There are two smaller reasons that support `[]` vs `[:]`:
1. Empty lists are generally used more than empty objects, i.e lists are built more often from scratch than objects are.
2. If you need to, later on, convert an empty object to a non-empty one, you already have a `:` in there. :)

#### How-to for common object tasks
Here's a quick overview of some common object tasks.

##### Adding a new field, changing existing fields
```ruby
obj.newKey = newValue
obj.newKey = updatedValue
```

##### Removing a field
```ruby
obj.key = null
```

##### Checking if object contains a key
```ruby
"key" in obj
```

##### Object class methods
* *size* - return the size (number of fields) of this object.
* *keys* - list of object keys as strings. Order is undefined.
* *values* - list of values. Order is undefined.
* *zip* - returns an iterable that returns key-value pairs as two-element lists when you iterate through it.
* *zipped* - *zip* collected to a list.
* *isEmpty* - returns true if the object's size is zero. Its counterpart method is *isNotEmpty*.
* *clear* - removes all fields from the object. Works on mutable objects only.
* *lock* - irreversibly converts a mutable object to immutable one.
* *merge* - adds all fields from other object to this one. Works on mutable objects only.
```ruby
obj = $[a = 3, b = "str", c = fn = 2]
obj.size() # 3
obj.keys() # ["a", "b", "c"]
obj.values() # [3, "str", fn = 2]
obj.zipped() # [[a, 3], [b, "str"], [c, fn = 2]]
obj.isEmpty() # false
obj.merge([a = 10, d = 20]) # obj is now $[a = 3, b = "str", c = fn = 2, d = 20]
obj.clear() # obj is now $[]
obj.lock() # obj is now []
```
#### How-to for common object tasks
Here's a quick overview of some common object tasks.

##### Adding a new field, changing existing fields
```ruby
obj.newKey = newValue # For mutable objects only
obj.newKey = updatedValue # For mutable objects only
```

##### Removing a field
```ruby
obj.key = nil # For mutable objects only
```

##### Checking if object contains a key
```ruby
"key" in obj
```

##### Object class methods
* *size* - return the size (number of fields) of this object.
* *keys* - list of object keys as strings. Order is undefined.
* *values* - list of values. Order is undefined.
* *zip* - returns an iterable that returns key-value pairs as two-element lists when you iterate through it.
* *zipped* - *zip* collected to a list.
* *isEmpty* - returns true if the object's size is zero. Its counterpart method is *isNotEmpty*.
* *clear* - removes all fields from the object. Works on mutable objects only.
* *lock* - irreversibly converts a mutable object to immutable one.
* *merge* - adds all fields from other object to this one. Works on mutable objects only.
```ruby
obj = $[a = 3, b = "str", c = fn = 2]
obj.size() # 3
obj.keys() # ["a", "b", "c"]
obj.values() # [3, "str", fn = 2]
obj.zipped() # [[a, 3], [b, "str"], [c, fn = 2]]
obj.isEmpty() # false
obj.merge([a = 10, d = 20]) # obj is now $[a = 3, b = "str", c = fn = 2, d = 20]
obj.clear() # obj is now $[]
obj.lock() # obj is now []
```

## Classes and instances   
NS is a fully object-oriented languages, meaning that classes are front and central. You'll spend a lot of time with them. All objects are instances of classes, including raw objects and lists. Numbers, booleans and strings also get boxed into instances.

Define a class with the *class* keyword and a name:
```ruby
class MyClass

class ClassWithABody {
}
```

You can omit braces if the class doesn't have a body. Classes are constants, i.e you can't reassign the class' identifier to another value later on. By convention, class names are capitalized.

### Methods
Methods are functions bound to an object. You declare them as regular functions within the class body, but omitting the *fn* part:
```ruby
class MyClass {
  method1(a, b, c) {
    ...
  }

  method2() => @method1(2, 3, 4)
}
```

Again, the main difference is that methods are bound to the object on which they're invoked - be it the class itself or its instance. This means that *this* won't point to the function, but to its caller.

Subclasses can override methods by re-declaring them with the same signature - same name, arity and argument composition.

If you want to have at least runtime assurance that a method will be overridden before it's used, have it throw an error:
```ruby
class AbstractClass {
  pleaseOverrideMe(a, b, c) {
    throw "Not implemented!"
  }
}
```

### Instantiating and initializers
To instantiate a class, just call it as you would a function:
```ruby
instance = MyClass()
```

Instantiating a class implicitly calls the **init** method. Even if you didn't define one, each class has an implicit, argument-less *init* with an empty body. Overriding *init* allows you to add additional initialization logic to your class, as well as to pass arguments to it:
```ruby
class MyDate {
  init(timestamp) {
    # other logic here
  }
}
date = MyDate(100505050)
```

Some notes on *init*:
* *init* is just a regular method, meaning it can have any number of arguments, including default arguments. It also has access to *this* and *super*.
* *init* can't return early (that's a compile-time error) and always implicitly returns the instance at the end.

A common pattern is to pass values to the initializer in order to bind them to the instance's fields. Take, for example, the Range class:
```ruby
class Range {
  init(start, stop) {
    @start = start
    @stop = stop
  }
}
```

NS offers a simpler, more convenient syntax for this - if you wish to bind an *init* argument to a same-named field in the instance, just mark it with *@*:
```ruby
class Range {
  init(@start, @stop) # Same as the example above
}
```

*@* arguments don't differ from other arguments in any way, and can have default values. The code that binds these arguments to fields executes at *init* start.

If a class defines any initialized fields, they're assigned in the *init* body, after any bound arguments are set (with compiler synthesizing the code). This allows you to use *self* and *super* when assigning fields. Field assignments happen in the order in which they were typed.
```ruby
class MyClass is Supeclass {
  field1 = "some field"
  field2 = @field1.substring(2)
  field3 = super.method1()
}

# This if fully equivalent to:

class MyClass is Supeclass {
  init {
    @field1 = "some field"
    @field2 = @field1.substring(2)
    @field3 = super.method1()
  }
}
```

### Inheritance
NS supports single inheritance. Name the superclass after the *is* keyword:
```ruby
class Subclass is Superclass
```

Each and every class you type will silently inherit the *Object* class, even if you don't specify it. This means that every instance has access to all the *Object* methods by default.

> *Design note:* I opted for *is* to indicate inheritance as I feel it's important to stress the *is a* relationship between superclasses and subclasses. Lot of inheritance overuse (== misuse) comes from the lack of understanding of this concept - only inherit if the type you're specifying *is a* type it inherits.


You can access the superclass(es) and their fields with the *super* keyword. The super expression can have a specifier in parentheses to indicate which superclass should it refer to:
```ruby
class Subclass is Superclass {
  method {
    super # the Superclass class
    super.superClassMethod() # invokes the superclass method on Superclass
  }
}
```

Naturally, all *super* calls execute with *this* found to the subclass instance.

### Implicit @
By now you know that setters always have @ before the field name - that's how the compiler knows that we're setting a field here instead of declaring/assigning to a local variable.

When it comes to getter, however, the compiler is a bit smarter and allows you to omit the @ when referring to instance fields. More specifically, when encountering an identifier that might either be a variable or an instance field, it looks up its list of declared fields for this class. The list contains:
1. Any field, method or inner class declared *up to this point*.
1. All setters in the *init* method, including bound args.

### Implicit invocations
NS compiler will attempt to compile each get operation as a parameterless invocation. This allows you to use parameterless expression methods as computed properties:
```ruby
class Circle {
  init(@radius)

  circumference => 2 * radius * Math.PI
}

c = Circle(2)
Console.log(c.circumference) # implicit invocation
Console.log(c.circumference()) # explicit invocation
```

While this works well enough most of the time, there's a rare case where you want to actually get a reference to the method as opposed to invoking it. In that case, use a special double colon operator, *::*, instead of a *.*:
```ruby
class MyClass {
  init(@a)

  predicate => a > 3

  filter {
    array = otherArray.where(this::predicate)
  }
}
```

## Modules
This will be a short one. NS allows you to import code from another NS file using the *import* statement:
```ruby
import "pathToOtherFile"
```

The path represents a relative path from the file to which you're importing to the file being imported. You can omit the *.ns* extension from the path.

When compilation starts, NS compiler first does a pre-compile run to figure out all import statements and creates a hierarchy that assures that all the code is compiled and executed before the code that imports it. This also means that circular imports can't exist.

## Exception handing
Exception handing in NS is purposely an afterthought - since NS script are generally atomic (e.g, an effect class) and run in a very tight environment (called in-game in specific circumstainces), without many use-cases or developer users, they can and should be thoroughly tested so that no exceptions are produced. Still, some scaffolding exists to allow for declaring and handling exception code paths.

### Throwing exceptions
An exception is just a value (can be any value) that's *thrown* instead of being *returned*:
```ruby
fn myFn(a) {
  if a < 0 {
    throw "Not a legal value! $a"
  }
}
```

The *throw* keywords acts exactly as *return* does, except it wraps its value in a special *Thrown* construct that signals the VM that this value was thrown and has to be handled somewhere down the line. Attempting to interact with a thrown value is a runtime error.

### Exception coalescence
The first stage in handling an exception is to try to coalesce it to a different value using the *?!* operator:
```ruby
fn iThrow(a) {
  if a < 0 {
    throw "Invalid!"
  }
  return a + 10
}
result = iThrow(-2) ?! 0
```

The *?!* operator behaves exactly the same as *??* does for nulls, except it works on Thrown values.

### Rethrowing
The exception coalescence operator can be used to rethrow an exception. Basically, if an exception is found on the left-hand side of the expression, it'll be immediately thrown and the call site will exit:
```ruby
fn iThrow(a) {
  if a < 0 {
    throw "Invalid!"
  }
  return a + 10
}
fn testRethrow {
  Console.println("In rethrow")
  result = iThrow(-1) ?! throw # rethrow
}
Console.println(testRethrow())
```

### Complex exception handling - when throw
If you really need to inspect the thrown value and make decision based on it, you can unpack it with the *when throw* expression:
```ruby
fn multithrow(a) {
  if a < 0 {
    throw "Negative"
  }
  if a == 0 {
    throw "Zero"
  }
  return a + 1
}

message = when throw multithrow(someVariable) {
  "Negative" => "The value was negative"
  "Zero" => "The value was zero"
  else => "No exception thrown"
}
```