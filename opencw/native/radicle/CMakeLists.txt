cmake_minimum_required(VERSION 3.25)

project(GardenRadicle LANGUAGES C CXX)

include(FetchContent)

set(RADICLE_COMMIT 62d7694370c20bf6fd54c7c8bd9a303c3c8df24a)
set(RADICLE_BIN_DIR "${CMAKE_CURRENT_SOURCE_DIR}/bin")

find_package(ZLIB QUIET)
if(NOT TARGET ZLIB::ZLIB)
  set(ZLIB_BUILD_EXAMPLES OFF CACHE BOOL "" FORCE)
  FetchContent_Declare(garden_zlib
    GIT_REPOSITORY https://github.com/madler/zlib.git
    GIT_TAG 51b7f2abdade71cd9bb0e7a373ef2610ec6f9daf)
  FetchContent_MakeAvailable(garden_zlib)
  set_target_properties(zlibstatic PROPERTIES POSITION_INDEPENDENT_CODE ON)
  target_include_directories(zlibstatic INTERFACE "${garden_zlib_SOURCE_DIR}" "${garden_zlib_BINARY_DIR}")
  add_library(ZLIB::ZLIB ALIAS zlibstatic)
endif()

set(RADICLE_BUILD_TOOLS OFF CACHE BOOL "" FORCE)
set(RADICLE_BUILD_TESTS OFF CACHE BOOL "" FORCE)
set(RADICLE_INSTALL OFF CACHE BOOL "" FORCE)
FetchContent_Declare(radicle
  GIT_REPOSITORY https://github.com/G-A-R-D-E-N/GardenTools.git
  GIT_TAG ${RADICLE_COMMIT}
  SOURCE_DIR "${CMAKE_CURRENT_SOURCE_DIR}/src"
  SOURCE_SUBDIR Radicle)
FetchContent_MakeAvailable(radicle)

set_property(TARGET radicle_c PROPERTY VERSION)
set_property(TARGET radicle_c PROPERTY SOVERSION)
set_target_properties(radicle_c PROPERTIES
  RUNTIME_OUTPUT_DIRECTORY "$<1:${RADICLE_BIN_DIR}>"
  LIBRARY_OUTPUT_DIRECTORY "$<1:${RADICLE_BIN_DIR}>")
file(WRITE "${RADICLE_BIN_DIR}/Radicle.props"
  "<Project><PropertyGroup><RadicleSourceDir>${radicle_SOURCE_DIR}</RadicleSourceDir></PropertyGroup></Project>\n")
