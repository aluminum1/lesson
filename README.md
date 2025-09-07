# lesson

C# terminal app that takes a pdf file containing handwritten notes (eg lecture notes, created in an app like Notability or Inkscape or Goodnotes) with special rectangle placeholders containing the web addresses for

 * videos (.mp4 files)
 * unity web apps

and outputs a standalone html file (no server needed) displaying the lecture notes with the above dynamic content embedded. This html file can be shared
via email etc as you would a pdf, but it now contains the interactive video and demos. 

# Example usage

```
dotnet run lecture.pdf
```

will produce a standalone [index.html](https://aluminum1.github.io/lesson/index.html) file (no web server needed) with the content loaded. 
